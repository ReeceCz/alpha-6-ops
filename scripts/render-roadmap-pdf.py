"""Render docs/web-roadmap.md to a PDF with the installed Chrome.

Usage: python scripts/render-roadmap-pdf.py [output.pdf]
Handles the Markdown subset the roadmap uses: headings, paragraphs, bullet and numbered lists (one level of
nesting by indentation), pipe tables, horizontal rules, bold, italic, inline code and links. No dependencies.
"""
import html
import os
import re
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, "docs", "web-roadmap.md")
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"

CSS = """
@page { size: A4; margin: 18mm 16mm 20mm 16mm; }
body { font: 10.5pt/1.5 "Segoe UI", "Helvetica Neue", Arial, sans-serif; color: #1b2230; max-width: 100%; }
h1 { font-size: 24pt; margin: 0 0 4pt; color: #0f2a4a; letter-spacing: -0.01em; }
h1 + p { color: #4a5568; margin-top: 0; }
h2 { font-size: 16pt; margin: 22pt 0 8pt; color: #0f2a4a; border-bottom: 2px solid #f2c94c; padding-bottom: 3pt; page-break-after: avoid; }
h3 { font-size: 12.5pt; margin: 16pt 0 6pt; color: #16365c; page-break-after: avoid; }
p { margin: 0 0 7pt; }
ul, ol { margin: 0 0 8pt 0; padding-left: 18pt; }
li { margin: 0 0 3pt; }
li > ul, li > ol { margin-top: 3pt; }
code { font: 9.3pt "Cascadia Mono", Consolas, monospace; background: #eef2f7; padding: 0 3px; border-radius: 3px; }
table { border-collapse: collapse; width: 100%; margin: 6pt 0 10pt; font-size: 9.6pt; page-break-inside: auto; }
th, td { border: 1px solid #cfd8e3; padding: 4pt 6pt; vertical-align: top; text-align: left; }
th { background: #0f2a4a; color: #fff; font-weight: 600; }
tr { page-break-inside: avoid; }
hr { border: 0; border-top: 1px solid #cfd8e3; margin: 16pt 0; }
strong { color: #0f2a4a; }
a { color: #1f5fa8; text-decoration: none; }
.cover { margin-bottom: 18pt; }
.cover .kicker { color: #b8860b; font-weight: 600; letter-spacing: 0.04em; margin-bottom: 2pt; }
h2 { page-break-before: auto; }
"""


def inline(text: str) -> str:
    text = html.escape(text, quote=False)
    text = re.sub(r"`([^`]+)`", r"<code>\1</code>", text)
    text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(r"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", r"<em>\1</em>", text)
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', text)
    text = re.sub(r"(?<![\"'>])(https?://[^\s<)]+)", r'<a href="\1">\1</a>', text)
    return text


def convert(md: str) -> str:
    out = []
    lines = md.splitlines()
    i = 0
    para: list[str] = []
    list_stack: list[tuple[str, int]] = []  # (tag, indent)

    def flush_para():
        if para:
            out.append("<p>" + inline(" ".join(s.strip() for s in para)) + "</p>")
            para.clear()

    def close_lists(to_indent=-1):
        while list_stack and list_stack[-1][1] > to_indent:
            tag, _ = list_stack.pop()
            out.append(f"</li></{tag}>")

    while i < len(lines):
        line = lines[i]
        stripped = line.strip()
        m_list = re.match(r"^(\s*)([-*]|\d+\.)\s+(.*)$", line)
        if not stripped:
            flush_para()
            # A blank line ends a list only if the next non-blank line is not a list item or continuation.
            j = i + 1
            while j < len(lines) and not lines[j].strip():
                j += 1
            nxt = lines[j] if j < len(lines) else ""
            if not re.match(r"^\s*([-*]|\d+\.)\s+", nxt) and not (list_stack and nxt.startswith("  ")):
                close_lists()
            i += 1
            continue
        if stripped.startswith("---") and set(stripped) <= {"-"}:
            flush_para(); close_lists(); out.append("<hr>"); i += 1; continue
        m_h = re.match(r"^(#{1,6})\s+(.*)$", line)
        if m_h:
            flush_para(); close_lists()
            level = len(m_h.group(1))
            out.append(f"<h{level}>{inline(m_h.group(2))}</h{level}>")
            i += 1; continue
        if stripped.startswith("|"):
            flush_para(); close_lists()
            rows = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                rows.append([c.strip() for c in lines[i].strip().strip("|").split("|")])
                i += 1
            header, body = rows[0], [r for r in rows[1:] if not all(set(c) <= {"-", ":"} for c in r)]
            out.append("<table><thead><tr>" + "".join(f"<th>{inline(c)}</th>" for c in header) + "</tr></thead><tbody>")
            for r in body:
                out.append("<tr>" + "".join(f"<td>{inline(c)}</td>" for c in r) + "</tr>")
            out.append("</tbody></table>")
            continue
        if m_list:
            flush_para()
            indent = len(m_list.group(1))
            tag = "ol" if m_list.group(2)[0].isdigit() else "ul"
            if list_stack and list_stack[-1][1] == indent:
                out.append("</li>")
            elif list_stack and list_stack[-1][1] > indent:
                close_lists(indent)
                out.append("</li>")
            else:
                out.append(f"<{tag}>")
                list_stack.append((tag, indent))
            item = [m_list.group(3)]
            i += 1
            # continuation lines (indented, not a new item) join the item before inline formatting so code spans
            # and emphasis can wrap across lines
            while i < len(lines) and lines[i].strip() and not re.match(r"^\s*([-*]|\d+\.)\s+", lines[i]) and lines[i].startswith(" " * (indent + 2)):
                item.append(lines[i].strip())
                i += 1
            out.append("<li>" + inline(" ".join(item)))
            continue
        if list_stack and line.startswith("  "):
            out.append(" " + inline(stripped)); i += 1; continue
        close_lists()
        para.append(line)
        i += 1
    flush_para(); close_lists()
    return "\n".join(out)


def main():
    target = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "outputs", "Alpha6OPS-Web-Roadmap-2026-09-18.pdf")
    md = open(SOURCE, encoding="utf-8").read()
    body = convert(md)
    doc = f"<!doctype html><html><head><meta charset='utf-8'><title>Alpha 6 OPS — Web and Accounts Roadmap</title><style>{CSS}</style></head><body>{body}</body></html>"
    os.makedirs(os.path.dirname(target), exist_ok=True)
    with tempfile.NamedTemporaryFile("w", suffix=".html", delete=False, encoding="utf-8") as f:
        f.write(doc)
        html_path = f.name
    try:
        subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--no-pdf-header-footer",
                        f"--print-to-pdf={target}", "file:///" + html_path.replace("\\", "/")], check=True, timeout=120,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    finally:
        os.unlink(html_path)
    print(target, os.path.getsize(target), "bytes")


if __name__ == "__main__":
    main()
