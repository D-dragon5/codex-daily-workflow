import datetime as dt
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET
import zipfile

MODULE = Path(__file__).resolve().parents[1]
SKILL = MODULE / "skills" / "daily-review"
spec = importlib.util.spec_from_file_location("review_refresh", SKILL / "scripts" / "refresh.py")
refresh = importlib.util.module_from_spec(spec)
spec.loader.exec_module(refresh)


def ledger(*rows):
    return "\n".join([
        "# 测试总账", refresh.TASK_START,
        "| ID | 类别 | 任务 | 优先级 | 截止日期 | 状态 | 下一步 | 详细说明 |",
        "|---|---|---|---|---|---|---|---|", *rows, refresh.TASK_END,
        "## 历史", "这里的历史不能被导出脚本修改。",
    ])


class ReviewTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.config = self.root / "workflow.config.json"
        self.config.write_text(json.dumps({
            "schemaVersion": 1, "timezone": "UTC", "ledgerPath": "data/ledger.md",
            "tasksPath": "views/tasks.json", "detailsPath": "views/details.html",
            "overviewPath": "views/overview.xlsx",
        }), encoding="utf-8")
        self.source = self.root / "data" / "ledger.md"
        self.source.parent.mkdir()
        self.source.write_text(ledger(
            "| R01 | 科研 | 对照实验 | 重要 | 2030-04-02 | 进行中 | 运行最小样例 | 保存日志 |",
            "| C01 | 课程 | 示例展示 | 常规 | 2030-03-30 | 已完成（用户确认） | 无 | 回执未另查 |",
        ), encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def test_template_is_parseable_and_explicitly_synthetic(self):
        text = (SKILL / "assets" / "ledger-template.md").read_text(encoding="utf-8")
        self.assertEqual(3, len(refresh.parse_ledger(text)))
        self.assertIn("虚构示例", text)

    def test_escaped_cell_and_html_are_data(self):
        rows = refresh.parse_ledger(ledger(
            r'| A1 | 科研 | <script>bad</script>\|part | 重要 | 待定 | 未开始 | =SUM(1,2) | one<br>two |'
        ))
        self.assertEqual("<script>bad</script>|part", rows[0]["title"])
        self.assertEqual("one\ntwo", rows[0]["requirement"])
        page = refresh.make_html(rows, "history", self.source, dt.date(2030, 4, 1), "now").decode("utf-8")
        self.assertNotIn("<script>", page)
        self.assertIn("&lt;script&gt;", page)
        self.assertIn('id="task-A1"', page)

    def test_status_and_deadline_boundaries(self):
        rows = refresh.parse_ledger(ledger(
            "| A1 | 课程 | 部分完成 | 常规 | 2030-03-30 | 已完成初稿，未提交 | 核对 | 说明 |",
            "| A2 | 课程 | 已完成 | 常规 | 2030-03-30 | 已完成（用户确认） | 无 | 说明 |",
            "| A3 | 课程 | 今日截止 | 紧急 | 2030-04-01 | 未开始 | 核对 | 说明 |",
            "| A4 | 课程 | 候选时间 | 核查 | 2030-04-02或2030-04-03 | 待核查 | 核对 | 说明 |",
        ))
        today = dt.date(2030, 4, 1)
        self.assertEqual("日期已过·状态待核实", refresh.date_cue(rows[0], today))
        self.assertEqual("已完成", refresh.date_cue(rows[1], today))
        self.assertEqual("今天截止", refresh.date_cue(rows[2], today))
        self.assertIsNone(rows[3]["due"])

    def test_suggested_relative_or_uncertain_dates_remain_notes(self):
        notes = ["建议2030-04-01，实际日期待确认", "2030-04-01（待核实）", "明天", "2030-04-01或2030-04-02"]
        for note in notes:
            with self.subTest(note=note):
                task = refresh.parse_ledger(ledger("| sample | 工具 | 示例 | 常规 | %s | 待开始 | 确认 | 示例 |" % note))[0]
                self.assertIsNone(task["due"])
                self.assertEqual(note, task["deadlineNote"])
        for note in ["2030-04-01", "截止：2030-04-01前", "截止日期2030-04-01"]:
            task = refresh.parse_ledger(ledger("| sample | 工具 | 示例 | 常规 | %s | 待开始 | 确认 | 示例 |" % note))[0]
            self.assertEqual("2030-04-01", task["due"])

    def test_partial_completion_is_not_a_completed_task(self):
        for status in ["已完成（第一部分）", "已完成，仍待提交", "已完成第一部分"]:
            task = refresh.parse_ledger(ledger("| sample | 课程 | 示例 | 常规 | 待定 | %s | 继续完成 | 示例 |" % status))[0]
            self.assertEqual(status, task["status"])
            self.assertNotEqual("已完成", task["status"])

    def test_existing_english_complete_statuses_remain_supported(self):
        for status in ["done", "completed", "DONE", "Completed"]:
            task = refresh.parse_ledger(ledger("| sample | 工作 | 示例 | 常规 | 待定 | %s | 无 | 示例 |" % status))[0]
            self.assertEqual("已完成", task["status"])
            self.assertEqual(status, task["progress"])

    def test_new_stable_ids_and_full_details_survive_refresh(self):
        self.source.write_text(ledger(
            "| alpha_1 | 工作 | 同名示例 | 重要 | 待定 | 进行中 | 第一步 | 甲说明<br>第二行 |",
            "| beta-2 | 课程 | 同名示例 | 常规 | 2030-04-02 | 待开始 | 第二步 | 乙说明 |",
        ), encoding="utf-8")
        refresh.refresh(self.config, dt.date(2030, 4, 1))
        self.source.write_text(ledger(
            "| beta-2 | 课程 | 重命名示例 | 常规 | 2030-04-02 | 进行中 | 新步骤 | 更新的乙说明 |",
            "| new_task_3 | 工具 | 新增示例 | 常规 | 待定 | 待开始 | 检查新项 | 新增完整说明 |",
            "| alpha_1 | 工作 | 同名示例 | 重要 | 待定 | 进行中 | 第一步 | 甲说明<br>第二行 |",
        ), encoding="utf-8")
        refresh.refresh(self.config, dt.date(2030, 4, 1))
        tasks = json.loads((self.root / "views/tasks.json").read_text(encoding="utf-8"))["tasks"]
        self.assertEqual(["beta-2", "new_task_3", "alpha_1"], [task["id"] for task in tasks])
        by_id = {task["id"]: task for task in tasks}
        self.assertEqual("更新的乙说明", by_id["beta-2"]["requirement"])
        self.assertEqual("甲说明\n第二行", by_id["alpha_1"]["requirement"])
        self.assertEqual("新增完整说明", by_id["new_task_3"]["requirement"])
        self.assertEqual("待开始", by_id["new_task_3"]["progress"])
        page = (self.root / "views/details.html").read_text(encoding="utf-8")
        self.assertIn('id="task-new_task_3"', page)
        self.assertIn("新增完整说明", page)
        with zipfile.ZipFile(self.root / "views/overview.xlsx") as archive:
            detail = archive.read("xl/worksheets/sheet2.xml").decode("utf-8")
            for task in tasks:
                self.assertIn(task["id"], detail)
                self.assertIn(task["requirement"], detail)

    def test_malformed_edit_preserves_all_existing_views(self):
        refresh.refresh(self.config, dt.date(2030, 4, 1))
        paths = [self.root / "views" / name for name in ["tasks.json", "details.html", "overview.xlsx"]]
        before = {path: path.read_bytes() for path in paths}
        self.source.write_text(ledger("| broken | too few columns |"), encoding="utf-8")
        with self.assertRaises(refresh.LedgerError):
            refresh.refresh(self.config)
        for path in paths:
            self.assertEqual(before[path], path.read_bytes())

    def test_invalid_or_duplicate_tasks_fail_without_touching_views(self):
        target = self.root / "views" / "tasks.json"
        target.parent.mkdir()
        target.write_bytes(b"previous data")
        self.source.write_text(ledger(
            "| A1 | 课程 | 一 | 常规 | 待定 | 未开始 | 核对 | 说明 |",
            "| A1 | 课程 | 二 | 常规 | 待定 | 未开始 | 核对 | 说明 |",
        ), encoding="utf-8")
        with self.assertRaises(refresh.LedgerError):
            refresh.refresh(self.config)
        self.assertEqual(b"previous data", target.read_bytes())
        with self.assertRaises(refresh.LedgerError):
            refresh.parse_ledger(ledger("| A1 | 科研 | 一 | 常规 | 2030-02-30 | 未开始 | 核对 | 说明 |"))

    def test_check_only_has_no_write_side_effects(self):
        before = self.source.read_bytes()
        result = refresh.refresh(self.config, dt.date(2030, 4, 1), check_only=True)
        self.assertEqual("valid", result["status"])
        self.assertFalse((self.root / "views").exists())
        self.assertEqual(before, self.source.read_bytes())

    def test_export_links_dates_and_cached_completion(self):
        before = self.source.read_bytes()
        result = refresh.refresh(self.config, dt.date(2030, 4, 1))
        self.assertEqual(1, result["completed"])
        payload = json.loads((self.root / "views" / "tasks.json").read_text(encoding="utf-8"))
        self.assertEqual(["R01", "C01"], [task["id"] for task in payload["tasks"]])
        self.assertEqual("已完成", payload["tasks"][1]["status"])
        self.assertEqual("已完成（用户确认）", payload["tasks"][1]["progress"])
        self.assertEqual(before, self.source.read_bytes())
        ns = {"m": refresh.MAIN_NS}
        with zipfile.ZipFile(self.root / "views" / "overview.xlsx") as archive:
            for name in archive.namelist():
                if name.endswith((".xml", ".rels")):
                    ET.fromstring(archive.read(name))
            sheet = ET.fromstring(archive.read("xl/worksheets/sheet1.xml"))
            lookup = {item.attrib["r"]: item for item in sheet.findall(".//m:c", ns)}
            self.assertEqual("1天后截止", lookup["E7"].find("m:v", ns).text)
            self.assertEqual("已完成", lookup["E8"].find("m:v", ns).text)
            self.assertIn('F8="已完成"', lookup["E8"].find("m:f", ns).text)
            self.assertEqual(str(refresh.excel_serial(dt.date(2030, 4, 2))), lookup["D7"].find("m:v", ns).text)
            links = sheet.findall("m:hyperlinks/m:hyperlink", ns)
            self.assertEqual("'任务详情'!A5", [x for x in links if x.attrib["ref"] == "H7"][0].attrib["location"])
            rels = archive.read("xl/worksheets/_rels/sheet2.xml.rels").decode("utf-8")
            self.assertIn("#task-R01", rels)
            self.assertNotIn("HYPERLINK", archive.read("xl/worksheets/sheet2.xml").decode("utf-8"))

    def test_cell_text_cannot_become_a_formula(self):
        task = refresh.parse_ledger(ledger('| A1 | 课程 | =1+1 | 常规 | 待定 | 未开始 | +SUM(1,2) | 说明 |'))
        content = refresh.make_xlsx(task, dt.date(2030, 4, 1), self.source, self.root / "details.html")
        with zipfile.ZipFile(io.BytesIO(content)) as archive:
            sheet = ET.fromstring(archive.read("xl/worksheets/sheet1.xml"))
            ns = {"m": refresh.MAIN_NS}
            title = sheet.find('.//m:c[@r="B7"]', ns)
            self.assertEqual("inlineStr", title.attrib["t"])
            self.assertIsNone(title.find("m:f", ns))

    def test_partial_commit_rolls_back_old_views(self):
        first, second = self.root / "a.json", self.root / "b.json"
        first.write_bytes(b"old first")
        second.write_bytes(b"old second")
        original_replace = refresh.os.replace
        calls = 0
        def fail_second(source, target):
            nonlocal calls
            calls += 1
            if calls == 2:
                raise PermissionError("simulated locked target")
            return original_replace(source, target)
        with mock.patch.object(refresh.os, "replace", side_effect=fail_second):
            with self.assertRaises(PermissionError):
                refresh.publish_files({first: b"new first", second: b"new second"})
        self.assertEqual(b"old first", first.read_bytes())
        self.assertEqual(b"old second", second.read_bytes())
        self.assertFalse(list(self.root.glob(".workflow-stage-*")))

    def test_output_cannot_overwrite_ledger(self):
        value = json.loads(self.config.read_text())
        value["tasksPath"] = value["ledgerPath"]
        self.config.write_text(json.dumps(value), encoding="utf-8")
        with self.assertRaises(refresh.LedgerError):
            refresh.refresh(self.config)


if __name__ == "__main__":
    unittest.main()
