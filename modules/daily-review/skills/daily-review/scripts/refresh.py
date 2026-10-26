#!/usr/bin/env python3
"""Derive portable task views from a Markdown ledger; Python standard library only."""
from __future__ import annotations

import argparse
import datetime as dt
import html
import io
import json
import os
from pathlib import Path
import re
import sys
import tempfile
from typing import Dict, List, Optional
import zipfile
from xml.sax.saxutils import escape, quoteattr

HEADERS = ["ID", "类别", "任务", "优先级", "截止日期", "状态", "下一步", "详细说明"]
TASK_START, TASK_END = "<!-- tasks:start -->", "<!-- tasks:end -->"
MAIN_NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
REL_NS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
COMPLETE = {"已完成", "完成", "已验证完成", "已完成（用户确认）", "已完成（已验证）", "已完成(用户确认)", "已完成(已验证)"}


class LedgerError(ValueError):
    pass


def split_row(line: str) -> List[str]:
    """Parse escaped pipes without interpreting arbitrary Markdown/HTML."""
    text = line.strip()
    if not text.startswith("|") or not text.endswith("|"):
        raise LedgerError("任务表每行须以竖线开始和结束")
    fields, current, escaped = [], [], False
    for char in text[1:-1]:
        if escaped:
            if char not in "|\\":
                current.append("\\")
            current.append(char)
            escaped = False
        elif char == "\\":
            escaped = True
        elif char == "|":
            fields.append("".join(current).strip())
            current = []
        else:
            current.append(char)
    if escaped:
        current.append("\\")
    fields.append("".join(current).strip())
    return [re.sub(r"<br\s*/?>", "\n", item, flags=re.I) for item in fields]


def parse_ledger(content: str) -> List[dict]:
    if content.count(TASK_START) != 1 or content.count(TASK_END) != 1:
        raise LedgerError("总账必须恰好包含一组 tasks:start 和 tasks:end 标记")
    begin, end = content.index(TASK_START), content.index(TASK_END)
    if begin >= end:
        raise LedgerError("任务表标记顺序错误")
    lines = [line for line in content[begin + len(TASK_START):end].splitlines() if line.strip()]
    if len(lines) < 2 or split_row(lines[0]) != HEADERS:
        raise LedgerError("任务表表头应为：" + " / ".join(HEADERS))
    separator = split_row(lines[1])
    if len(separator) != len(HEADERS) or not all(re.fullmatch(r":?-{3,}:?", value.replace(" ", "")) for value in separator):
        raise LedgerError("任务表分隔行无效")
    tasks, seen = [], set()
    for number, line in enumerate(lines[2:], start=3):
        row = split_row(line)
        if len(row) != len(HEADERS):
            raise LedgerError("任务表第 %d 行列数错误；单元格竖线须转义为 \\|" % number)
        task_id, category, title, priority, deadline, status, next_step, description = row
        if not re.fullmatch(r"[A-Za-z][A-Za-z0-9_-]{0,63}", task_id):
            raise LedgerError("任务 ID 无效：" + task_id)
        if task_id in seen:
            raise LedgerError("任务 ID 重复：" + task_id)
        if not title:
            raise LedgerError("任务标题不能为空：" + task_id)
        seen.add(task_id)
        match = re.fullmatch(r"(?:(?:截止(?:日期|时间)?)[：:]?\s*)?(\d{4}-\d{2}-\d{2})(?:\s*(?:截止|前))?", deadline.strip())
        due = None
        if match:
            try:
                due = dt.date.fromisoformat(match[1]).isoformat()
            except ValueError as error:
                raise LedgerError("截止日期无效（%s）：%s" % (task_id, match[1])) from error
        # Relative, tentative and multi-candidate notes never become hard dates.
        tasks.append({
            "id": task_id, "title": title, "priority": priority or "常规", "category": category or "未分类",
            "due": due, "status": "已完成" if status in COMPLETE or status.lower() in {"done", "completed"} else (status or "待核查"),
            "next": next_step, "progress": status or "待核查", "requirement": description,
            "deadlineNote": deadline or "日期待定",
        })
    return tasks


def date_cue(task: dict, today: dt.date) -> str:
    if task["status"] == "已完成":
        return "已完成"
    if not task["due"]:
        return "日期待定"
    days = (dt.date.fromisoformat(task["due"]) - today).days
    if days < 0:
        return "日期已过·状态待核实"
    return "今天截止" if days == 0 else "%d天后截止" % days


def config_path(base: Path, raw: str) -> Path:
    if not isinstance(raw, str) or not raw.strip():
        raise LedgerError("配置路径须为非空字符串")
    path = Path(os.path.expanduser(os.path.expandvars(raw)))
    return (path if path.is_absolute() else base / path).resolve()


def read_config(path: Path, include_xlsx: bool = True) -> dict:
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if value.get("schemaVersion") != 1:
        raise LedgerError("仅支持 schemaVersion=1")
    defaults = {
        "ledgerPath": "data/每日任务与科研总账.md", "tasksPath": "data/tasks.json",
        "overviewPath": "data/任务总览.xlsx", "detailsPath": "data/任务详情.html",
    }
    resolved = {key: config_path(path.parent, value.get(key, default)) for key, default in defaults.items()}
    targets = [resolved["tasksPath"], resolved["detailsPath"]]
    if include_xlsx:
        targets.append(resolved["overviewPath"])
    if len(set(targets)) != len(targets):
        raise LedgerError("派生文件的目标路径不能重复")
    if resolved["ledgerPath"] in targets or path.resolve() in targets:
        raise LedgerError("派生视图不能覆盖总账或配置文件")
    value["paths"] = resolved
    return value


def clock_now(timezone: str) -> dt.datetime:
    if timezone == "local":
        return dt.datetime.now().astimezone()
    if timezone in ("UTC", "Etc/UTC"):
        return dt.datetime.now(dt.timezone.utc)
    try:
        from zoneinfo import ZoneInfo
        return dt.datetime.now(ZoneInfo(timezone))
    except (ValueError, KeyError, ImportError) as error:
        raise LedgerError("无法解析时区 %s；请安装时区数据库或选择 local / UTC" % timezone) from error


def make_html(tasks: List[dict], ledger: str, ledger_path: Path, today: dt.date, updated: str) -> bytes:
    h = html.escape
    menu = "".join('<a href="#task-%s">%s</a>' % (h(t["id"]), h(t["title"])) for t in tasks)
    cards = []
    for task in tasks:
        values = [("分类与优先级", task["category"] + " · " + task["priority"]),
                  ("当前状态", task["progress"]), ("截止依据", task["deadlineNote"]),
                  ("临近情况", date_cue(task, today)), ("下一步", task["next"]), ("详细说明", task["requirement"])]
        cards.append('<section id="task-%s"><small>%s</small><h2>%s</h2><dl>%s</dl><a href="#top">返回目录</a></section>' % (
            h(task["id"]), h(task["id"]), h(task["title"]),
            "".join("<dt>%s</dt><dd>%s</dd>" % (h(key), h(value)) for key, value in values)))
    document = """<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>任务详情</title><style>body{margin:0;background:#f3f6fa;color:#23354b;font:16px/1.7 system-ui,'Microsoft YaHei',sans-serif}main{max-width:1050px;margin:auto;padding:26px 20px}h1{margin-bottom:4px}a{color:#245f9b}nav{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:9px;margin:24px 0}nav a,section,details{background:white;border:1px solid #dce4ee;border-radius:8px;padding:15px}section{padding:22px;margin:18px 0;scroll-margin-top:16px}section:target{outline:3px solid #e8ba60}dl{display:grid;grid-template-columns:115px 1fr;gap:12px}dt{font-weight:600;color:#586d86}dd{margin:0;white-space:pre-wrap;overflow-wrap:anywhere}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:14px/1.7 system-ui,sans-serif}small{color:#60738c}@media(max-width:600px){dl{grid-template-columns:1fr}dd{margin-bottom:12px}}</style>
<main id="top"><h1>任务详情</h1><p>基准日期：%s · 生成：%s</p><a href="%s">打开原始总账</a><nav>%s</nav>%s<details><summary>完整总账与历史</summary><pre>%s</pre></details></main></html>""" % (
        today.isoformat(), h(updated), h(ledger_path.as_uri()), menu, "".join(cards), h(ledger))
    return document.encode("utf-8")


def excel_serial(value: dt.date) -> int:
    return (value - dt.date(1899, 12, 30)).days


def cell(ref: str, value, style: int = 0, formula: Optional[str] = None) -> str:
    if formula is not None:
        return '<c r="%s" s="%d" t="str"><f>%s</f><v>%s</v></c>' % (ref, style, escape(formula), escape(str(value)))
    if value is None:
        return '<c r="%s" s="%d"/>' % (ref, style)
    if isinstance(value, (int, float)):
        return '<c r="%s" s="%d"><v>%s</v></c>' % (ref, style, value)
    return '<c r="%s" s="%d" t="inlineStr"><is><t xml:space="preserve">%s</t></is></c>' % (ref, style, escape(str(value)))


def row(number: int, cells: List[str], height: int = 30) -> str:
    return '<row r="%d" ht="%d" customHeight="1">%s</row>' % (number, height, "".join(cells))


def styles_xml() -> str:
    fonts = [
        '<font><sz val="11"/><name val="Microsoft YaHei"/><color rgb="FF23354B"/></font>',
        '<font><b/><sz val="18"/><name val="Microsoft YaHei"/><color rgb="FF23354B"/></font>',
        '<font><b/><sz val="11"/><name val="Microsoft YaHei"/><color rgb="FFFFFFFF"/></font>',
        '<font><u/><sz val="11"/><name val="Microsoft YaHei"/><color rgb="FF245F9B"/></font>',
        '<font><b/><sz val="11"/><name val="Microsoft YaHei"/><color rgb="FF23354B"/></font>',
        '<font><sz val="11"/><name val="Microsoft YaHei"/><color rgb="FF6F7B8A"/></font>',
        '<font><b/><sz val="11"/><name val="Microsoft YaHei"/><color rgb="FF9A342D"/></font>',
        '<font><b/><sz val="11"/><name val="Microsoft YaHei"/><color rgb="FF8A5B15"/></font>',
    ]
    colors = ["FF263B55", "FFFCE7E5", "FFE5EEF9", "FFFFF0CD", "FFEFF2F5"]
    fills = '<fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill>' + "".join(
        '<fill><patternFill patternType="solid"><fgColor rgb="%s"/><bgColor indexed="64"/></patternFill></fill>' % color for color in colors)
    # index: base, title, header, link, date, urgent, important, check, completed, detail-label
    specs = [(0, 0, 0), (1, 0, 0), (2, 2, 0), (3, 0, 0), (0, 0, 164),
             (6, 3, 0), (4, 4, 0), (7, 5, 0), (5, 6, 0), (4, 0, 0)]
    xfs = "".join('<xf numFmtId="%d" fontId="%d" fillId="%d" borderId="0" xfId="0" applyFont="1" applyFill="1" applyNumberFormat="1" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf>' % (fmt, font, fill) for font, fill, fmt in specs)
    return '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="%s"><numFmts count="1"><numFmt numFmtId="164" formatCode="yyyy-mm-dd"/></numFmts><fonts count="8">%s</fonts><fills count="7">%s</fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="10">%s</cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>' % (MAIN_NS, "".join(fonts), fills, xfs)


def make_xlsx(tasks: List[dict], today: dt.date, ledger_path: Path, details_path: Path) -> bytes:
    overview_rows = [row(2, [cell("A2", "任务总览", 1)], 32),
                     row(3, [cell("A3", "基准日期", 9), cell("B3", excel_serial(today), 4), cell("G3", "打开任务详情网页", 3)], 24),
                     row(4, [cell("A4", "总账为唯一事实来源；日期已过而状态不明时仍待核查。")], 24),
                     row(6, [cell("%s6" % chr(65+i), value, 2) for i, value in enumerate(["优先级", "任务", "分类", "截止日期", "临近情况", "当前状态", "下一步", "详情", "ID"])], 28)]
    overview_links = ['<hyperlink ref="G3" r:id="rId1" display="打开任务详情网页"/>']
    details_rows = [row(2, [cell("A2", "任务详情", 1)], 32), row(3, [cell("B3", "打开原始总账", 3)], 24)]
    details_links = ['<hyperlink ref="B3" r:id="rId1" display="打开原始总账"/>']
    detail_external = [("rId1", ledger_path.as_uri())]
    priority_style = {"紧急": 5, "重要": 6, "核查": 7}
    for index, task in enumerate(tasks):
        r, dr = index + 7, index * 8 + 5
        done_style = 8 if task["status"] == "已完成" else 0
        formula = 'IF(F%d="已完成","已完成",IF(D%d="","日期待定",IF(D%d<$B$3,"日期已过·状态待核实",IF(D%d=$B$3,"今天截止",D%d-$B$3&"天后截止"))))' % (r, r, r, r, r)
        overview_rows.append(row(r, [
            cell("A%d" % r, task["priority"], 8 if done_style else priority_style.get(task["priority"], 0)),
            cell("B%d" % r, task["title"], done_style), cell("C%d" % r, task["category"], done_style),
            cell("D%d" % r, excel_serial(dt.date.fromisoformat(task["due"])) if task["due"] else None, 4),
            cell("E%d" % r, date_cue(task, today), done_style, formula), cell("F%d" % r, task["status"], done_style),
            cell("G%d" % r, task["next"], done_style), cell("H%d" % r, "看详情", 3), cell("I%d" % r, task["id"], done_style),
        ], 40))
        overview_links.append('<hyperlink ref="H%d" location=%s display="看详情"/>' % (r, quoteattr("'任务详情'!A%d" % dr)))
        details_rows.append(row(dr, [cell("A%d" % dr, task["id"], 9), cell("B%d" % dr, task["title"], 9), cell("C%d" % dr, "返回总览", 3)], 28))
        details_links.append('<hyperlink ref="C%d" location=%s display="返回总览"/>' % (dr, quoteattr("'任务总览'!B%d" % r)))
        info = [("分类", task["category"] + " · " + task["priority"]), ("当前进展", task["progress"]),
                ("详细说明", task["requirement"]), ("下一步", task["next"]), ("截止依据", task["deadlineNote"]), ("网页定位", "打开此任务")]
        for offset, (key, value) in enumerate(info, start=1):
            details_rows.append(row(dr+offset, [cell("A%d" % (dr+offset), key, 9), cell("B%d" % (dr+offset), value, 3 if offset == 6 else 0)], 52 if offset in (2, 3, 4, 5) else 26))
        relation_id = "rId%d" % (index + 2)
        detail_external.append((relation_id, details_path.as_uri() + "#task-" + task["id"]))
        details_links.append('<hyperlink ref="B%d" r:id="%s" display="打开此任务"/>' % (dr+6, relation_id))
    widths = [10, 31, 14, 14, 26, 20, 48, 12, 12]
    overview_cols = "".join('<col min="%d" max="%d" width="%d" customWidth="1"/>' % (i+1, i+1, width) for i, width in enumerate(widths))
    detail_cols = '<col min="1" max="1" width="17" customWidth="1"/><col min="2" max="2" width="100" customWidth="1"/><col min="3" max="3" width="16" customWidth="1"/>'
    def worksheet(rows, cols, links, freeze, autofilter=""):
        return '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="%s" xmlns:r="%s"><sheetViews><sheetView workbookViewId="0" showGridLines="0"><pane ySplit="%d" topLeftCell="A%d" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><sheetFormatPr defaultRowHeight="24"/><cols>%s</cols><sheetData>%s</sheetData>%s<hyperlinks>%s</hyperlinks></worksheet>' % (MAIN_NS, REL_NS, freeze, freeze+1, cols, "".join(rows), autofilter, "".join(links))
    def relationships(items):
        return '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">%s</Relationships>' % "".join('<Relationship Id="%s" Type="%s/hyperlink" Target=%s TargetMode="External"/>' % (key, REL_NS, quoteattr(target)) for key, target in items)
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
        files = {
            "[Content_Types].xml": '<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>',
            "_rels/.rels": '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="%s/officeDocument" Target="xl/workbook.xml"/></Relationships>' % REL_NS,
            "xl/workbook.xml": '<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="%s" xmlns:r="%s"><sheets><sheet name="任务总览" sheetId="1" r:id="rId1"/><sheet name="任务详情" sheetId="2" r:id="rId2"/></sheets><calcPr calcId="191029" fullCalcOnLoad="1"/></workbook>' % (MAIN_NS, REL_NS),
            "xl/_rels/workbook.xml.rels": '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="%s/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="%s/worksheet" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Type="%s/styles" Target="styles.xml"/></Relationships>' % (REL_NS, REL_NS, REL_NS),
            "xl/worksheets/sheet1.xml": worksheet(overview_rows, overview_cols, overview_links, 6, '<autoFilter ref="A6:I%d"/>' % (len(tasks)+6)),
            "xl/worksheets/sheet2.xml": worksheet(details_rows, detail_cols, details_links, 3),
            "xl/worksheets/_rels/sheet1.xml.rels": relationships([("rId1", details_path.as_uri())]),
            "xl/worksheets/_rels/sheet2.xml.rels": relationships(detail_external),
            "xl/styles.xml": styles_xml(),
        }
        for name, data in files.items():
            archive.writestr(name, data.encode("utf-8"))
    return output.getvalue()


def publish_files(files: Dict[Path, bytes]) -> None:
    """Stage all outputs, then replace; restore changed outputs on a later failure."""
    staged, previous, committed = {}, {}, []
    try:
        for target, data in files.items():
            if target.exists() and not target.is_file():
                raise LedgerError("派生路径已被目录占用：" + str(target))
            previous[target] = target.read_bytes() if target.exists() else None
            target.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.NamedTemporaryFile(prefix=".workflow-stage-", dir=target.parent, delete=False) as output:
                output.write(data)
                output.flush()
                os.fsync(output.fileno())
                staged[target] = Path(output.name)
        for target, stage in staged.items():
            os.replace(str(stage), str(target))
            committed.append(target)
    except Exception as error:
        rollback_errors = []
        for target in reversed(committed):
            try:
                old = previous[target]
                if old is None:
                    target.unlink()
                else:
                    with tempfile.NamedTemporaryFile(prefix=".workflow-rollback-", dir=target.parent, delete=False) as output:
                        output.write(old)
                        restore = Path(output.name)
                    os.replace(str(restore), str(target))
            except Exception as rollback_error:
                rollback_errors.append(str(rollback_error))
        if rollback_errors:
            raise OSError("刷新失败且部分视图无法还原：%s；%s" % (error, "; ".join(rollback_errors))) from error
        raise
    finally:
        for stage in staged.values():
            if stage.exists():
                stage.unlink()


def refresh(config_file: Path, date: Optional[dt.date] = None, include_xlsx: bool = True, check_only: bool = False) -> dict:
    config_file = config_file.resolve()
    config = read_config(config_file, include_xlsx)
    paths = config["paths"]
    content = paths["ledgerPath"].read_text(encoding="utf-8-sig")
    tasks = parse_ledger(content)
    timezone = config.get("timezone", "local")
    now = clock_now(timezone)
    today = date or now.date()
    if check_only:
        return {"status": "valid", "count": len(tasks), "date": today.isoformat()}
    payload = {"schemaVersion": 1, "updatedAt": now.isoformat(timespec="seconds"), "timezone": timezone, "tasks": tasks}
    # JSON is committed last so the desktop reader sees the coherent latest state.
    files = {}
    if include_xlsx:
        files[paths["overviewPath"]] = make_xlsx(tasks, today, paths["ledgerPath"], paths["detailsPath"])
    files[paths["detailsPath"]] = make_html(tasks, content, paths["ledgerPath"], today, payload["updatedAt"])
    files[paths["tasksPath"]] = (json.dumps(payload, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    publish_files(files)
    return {"status": "ok", "count": len(tasks), "completed": sum(t["status"] == "已完成" for t in tasks), "date": today.isoformat(), "outputs": [str(path) for path in files]}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="从唯一总账刷新结构化任务、网页和电子表格。")
    parser.add_argument("--config", required=True, type=Path, help="工作流配置路径")
    parser.add_argument("--date", type=dt.date.fromisoformat, help="可选基准日 YYYY-MM-DD")
    parser.add_argument("--no-xlsx", action="store_true", help="只刷新JSON和网页")
    parser.add_argument("--check", action="store_true", help="只验证，不写文件")
    args = parser.parse_args(argv)
    try:
        print(json.dumps(refresh(args.config, args.date, not args.no_xlsx, args.check), ensure_ascii=False))
        return 0
    except (OSError, ValueError, KeyError) as error:
        print(json.dumps({"status": "error", "message": str(error)}, ensure_ascii=False), file=sys.stderr)
        return 1


if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    raise SystemExit(main())
