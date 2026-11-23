#!/usr/bin/env python3
"""
Markdown to DOCX converter for NoneadCSS user manual.
Handles: headings, paragraphs, bold/italic/code inline, lists, tables, code blocks, blockquotes, horizontal rules.
"""
import sys
import re
from pathlib import Path
from docx import Document
from docx.shared import Pt, Inches, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.oxml.ns import qn
from docx.oxml import OxmlElement


def set_cell_shading(cell, color_hex):
    tc_pr = cell._tc.get_or_add_tcPr()
    shd = OxmlElement('w:shd')
    shd.set(qn('w:val'), 'clear')
    shd.set(qn('w:color'), 'auto')
    shd.set(qn('w:fill'), color_hex)
    tc_pr.append(shd)


def add_bottom_border(paragraph, size=6, color="2F5496", space=1):
    p_pr = paragraph._p.get_or_add_pPr()
    p_bdr = OxmlElement('w:pBdr')
    bottom = OxmlElement('w:bottom')
    bottom.set(qn('w:val'), 'single')
    bottom.set(qn('w:sz'), str(size))
    bottom.set(qn('w:space'), str(space))
    bottom.set(qn('w:color'), color)
    p_bdr.append(bottom)
    p_pr.append(p_bdr)


def set_run_font(run, name_cn='微软雅黑', name_en='Calibri', size=11, bold=False, italic=False, color=None):
    run.font.name = name_en
    run.font.size = Pt(size)
    run.bold = bold
    run.italic = italic
    if color:
        run.font.color.rgb = RGBColor.from_string(color)
    rPr = run._element.get_or_add_rPr()
    rFonts = rPr.find(qn('w:rFonts'))
    if rFonts is None:
        rFonts = OxmlElement('w:rFonts')
        rPr.insert(0, rFonts)
    rFonts.set(qn('w:eastAsia'), name_cn)


def parse_inline(text, run_parent):
    """Parse inline markdown and add runs to paragraph."""
    # Protect code spans first
    code_spans = []
    def _store_code(m):
        code_spans.append(m.group(1))
        return f"\x00CODE{len(code_spans)-1}\x00"
    text = re.sub(r'`([^`]+)`', _store_code, text)

    # Now split by bold and italic markers. We'll do a simple regex-based approach
    # Pattern: **bold**, *italic*, [text](url), ![alt](url)
    pattern = re.compile(
        r'(\*\*[^*]+\*\*)'         # bold
        r'|(\*[^*]+\*)'             # italic
        r'|(\[[^\]]+\]\([^)]+\))'   # links
        r'|(\x00CODE\d+\x00)'       # code spans
        r'|(>?\s*💡|>?\s*🔒|>?\s*⚠️)'  # emoji with optional leading >
    )

    # We will process character by character for reliability
    i = 0
    tlen = len(text)
    buf = []

    def flush_buf():
        if buf:
            s = ''.join(buf)
            if s:
                run = run_parent.add_run(s)
                set_run_font(run)
            buf.clear()

    while i < tlen:
        # Bold **text**
        if i + 1 < tlen and text[i] == '*' and text[i+1] == '*':
            j = text.find('**', i + 2)
            if j != -1:
                flush_buf()
                bold_text = text[i+2:j]
                # Check for emoji-prefix inside bold (e.g. **💡 提示:**)
                run = run_parent.add_run(bold_text)
                set_run_font(run, bold=True)
                i = j + 2
                continue
        # Italic *text*
        elif text[i] == '*' and (i == 0 or text[i-1] != '*'):
            j = text.find('*', i + 1)
            if j != -1 and (j + 1 >= tlen or text[j+1] != '*'):
                flush_buf()
                italic_text = text[i+1:j]
                run = run_parent.add_run(italic_text)
                set_run_font(run, italic=True)
                i = j + 1
                continue
        # Link [text](url)
        elif text[i] == '[':
            m = re.match(r'\[([^\]]+)\]\(([^)]+)\)', text[i:])
            if m:
                flush_buf()
                link_text = m.group(1)
                link_url = m.group(2)
                # Add as hyperlink
                part = run_parent.part
                r_id = part.relate_to(link_url, 'http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink', is_external=True)
                hyperlink = OxmlElement('w:hyperlink')
                hyperlink.set(qn('r:id'), r_id)
                new_run = OxmlElement('w:r')
                rPr = OxmlElement('w:rPr')
                rFonts = OxmlElement('w:rFonts')
                rFonts.set(qn('w:eastAsia'), '微软雅黑')
                rFonts.set(qn('w:ascii'), 'Calibri')
                rPr.append(rFonts)
                sz = OxmlElement('w:sz')
                sz.set(qn('w:val'), '22')
                rPr.append(sz)
                color = OxmlElement('w:color')
                color.set(qn('w:val'), '2F5496')
                rPr.append(color)
                u = OxmlElement('w:u')
                u.set(qn('w:val'), 'single')
                rPr.append(u)
                new_run.append(rPr)
                t_el = OxmlElement('w:t')
                t_el.text = link_text
                new_run.append(t_el)
                hyperlink.append(new_run)
                run_parent._p.append(hyperlink)
                i += m.end()
                continue
        # Code span placeholder
        elif text[i] == '\x00':
            m = re.match(r'\x00CODE(\d+)\x00', text[i:])
            if m:
                flush_buf()
                idx = int(m.group(1))
                code_text = code_spans[idx]
                run = run_parent.add_run(code_text)
                set_run_font(run, name_en='Consolas', name_cn='Consolas', size=10, color='1F1F1F')
                # Add light gray background shading
                rPr = run._element.get_or_add_rPr()
                shd = OxmlElement('w:shd')
                shd.set(qn('w:val'), 'clear')
                shd.set(qn('w:color'), 'auto')
                shd.set(qn('w:fill'), 'F2F2F2')
                rPr.append(shd)
                i += m.end()
                continue
        buf.append(text[i])
        i += 1
    flush_buf()


def process_markdown(md_path: Path, docx_path: Path):
    md_text = md_path.read_text(encoding='utf-8')
    lines = md_text.split('\n')

    doc = Document()

    # Set default font
    style = doc.styles['Normal']
    style.font.name = 'Calibri'
    style.font.size = Pt(11)
    style._element.rPr.rFonts.set(qn('w:eastAsia'), '微软雅黑')

    # Set margins
    for section in doc.sections:
        section.top_margin = Inches(0.9)
        section.bottom_margin = Inches(0.8)
        section.left_margin = Inches(0.9)
        section.right_margin = Inches(0.9)

    i = 0
    n = len(lines)

    # Fenced code block state
    in_code = False
    code_lang = ''
    code_lines = []

    # Table state
    in_table = False
    table_rows = []

    # Blockquote state
    in_quote = False
    quote_lines = []

    # List state
    list_stack = []  # list of (marker_type, indent_level)
    # marker_type: 'ul' or 'ol' with counter

    def flush_code():
        nonlocal in_code, code_lang, code_lines
        if in_code:
            code_text = '\n'.join(code_lines)
            p = doc.add_paragraph()
            p.paragraph_format.left_indent = Inches(0.2)
            p.paragraph_format.space_before = Pt(4)
            p.paragraph_format.space_after = Pt(4)
            # Gray background via 1-cell table for visual
            tbl = doc.add_table(rows=1, cols=1)
            tbl.alignment = WD_TABLE_ALIGNMENT.LEFT
            cell = tbl.cell(0, 0)
            set_cell_shading(cell, 'F7F7F7')
            # Border
            tc_pr = cell._tc.get_or_add_tcPr()
            tcBorders = OxmlElement('w:tcBorders')
            for edge in ('top', 'left', 'bottom', 'right'):
                border = OxmlElement(f'w:{edge}')
                border.set(qn('w:val'), 'single')
                border.set(qn('w:sz'), '4')
                border.set(qn('w:space'), '0')
                border.set(qn('w:color'), 'CCCCCC')
                tcBorders.append(border)
            tc_pr.append(tcBorders)
            cell.text = ''
            cp = cell.paragraphs[0]
            run = cp.add_run(code_text if code_text else ' ')
            set_run_font(run, name_en='Consolas', name_cn='Consolas', size=9.5)
            in_code = False
            code_lang = ''
            code_lines = []

    def flush_table():
        nonlocal in_table, table_rows
        if in_table and table_rows:
            # table_rows[0] = header, table_rows[1] = separator, table_rows[2..] = data
            if len(table_rows) >= 2:
                header = table_rows[0]
                data_rows = table_rows[2:]
                num_cols = len(header)
                tbl = doc.add_table(rows=1 + len(data_rows), cols=num_cols)
                tbl.style = 'Light Grid Accent 1'
                tbl.alignment = WD_TABLE_ALIGNMENT.CENTER
                # Header
                hdr_cells = tbl.rows[0].cells
                for ci, text in enumerate(header):
                    if ci < num_cols:
                        hdr_cells[ci].text = ''
                        if text.strip():
                            p = hdr_cells[ci].paragraphs[0]
                            parse_inline(text.strip(), p)
                            for run in p.runs:
                                run.bold = True
                        set_cell_shading(hdr_cells[ci], '2F5496')
                        # Set header font color
                        for p in hdr_cells[ci].paragraphs:
                            for r in p.runs:
                                r.font.color.rgb = RGBColor.from_string('FFFFFF')
                # Data
                for ri, row in enumerate(data_rows):
                    cells = tbl.rows[ri + 1].cells
                    for ci, text in enumerate(row):
                        if ci < num_cols:
                            cells[ci].text = ''
                            if text.strip():
                                p = cells[ci].paragraphs[0]
                                parse_inline(text.strip(), p)
            in_table = False
            table_rows = []

    def flush_quote():
        nonlocal in_quote, quote_lines
        if in_quote and quote_lines:
            # Use 1-cell table with left border for blockquote
            tbl = doc.add_table(rows=1, cols=1)
            cell = tbl.cell(0, 0)
            tc_pr = cell._tc.get_or_add_tcPr()
            tcBorders = OxmlElement('w:tcBorders')
            for edge in ('top', 'bottom', 'right'):
                border = OxmlElement(f'w:{edge}')
                border.set(qn('w:val'), 'nil')
                tcBorders.append(border)
            left = OxmlElement('w:left')
            left.set(qn('w:val'), 'single')
            left.set(qn('w:sz'), '18')
            left.set(qn('w:space'), '8')
            left.set(qn('w:color'), '2F5496')
            tcBorders.append(left)
            tc_pr.append(tcBorders)
            set_cell_shading(cell, 'F5F7FB')
            cell.text = ''
            first = True
            for ql in quote_lines:
                # Strip leading '>' and optional space
                cleaned = re.sub(r'^>\s?', '', ql)
                if first:
                    p = cell.paragraphs[0]
                    first = False
                else:
                    p = cell.add_paragraph()
                parse_inline(cleaned, p)
                for run in p.runs:
                    run.font.color.rgb = RGBColor.from_string('404040')
            in_quote = False
            quote_lines = []

    def flush_list():
        nonlocal list_stack
        list_stack = []

    def split_table_row(line):
        line = line.strip()
        if line.startswith('|'):
            line = line[1:]
        if line.endswith('|'):
            line = line[:-1]
        return [c.strip() for c in line.split('|')]

    while i < n:
        line = lines[i]
        stripped = line.strip()

        # Skip HTML comments
        if stripped.startswith('<!--'):
            # Skip until -->
            while i < n and '-->' not in lines[i]:
                i += 1
            i += 1
            continue

        # Fenced code block
        if stripped.startswith('```'):
            flush_table()
            flush_quote()
            flush_list()
            if in_code:
                flush_code()
            else:
                in_code = True
                code_lang = stripped[3:].strip()
                code_lines = []
            i += 1
            continue
        if in_code:
            code_lines.append(line)
            i += 1
            continue

        # Horizontal rule
        if re.match(r'^-{3,}\s*$', stripped) or re.match(r'^\*{3,}\s*$', stripped):
            flush_table()
            flush_quote()
            flush_list()
            p = doc.add_paragraph()
            add_bottom_border(p, size=8, color="CCCCCC", space=1)
            i += 1
            continue

        # Table detection (starts with |)
        if stripped.startswith('|'):
            flush_quote()
            flush_list()
            row = split_table_row(stripped)
            if in_table:
                # Check if it's separator line (all cells are --- with optional :alignment:)
                if all(re.match(r'^:?-{3,}:?$', c) for c in row):
                    table_rows.append(row)
                else:
                    table_rows.append(row)
            else:
                # Look ahead: next line must be separator
                if i + 1 < n:
                    next_stripped = lines[i + 1].strip()
                    if next_stripped.startswith('|'):
                        next_row = split_table_row(next_stripped)
                        if all(re.match(r'^:?-{3,}:?$', c) for c in next_row):
                            in_table = True
                            table_rows = [row]
                            i += 1
                            continue
                # Not a table header: treat as paragraph
                flush_table()
                p = doc.add_paragraph()
                parse_inline(stripped, p)
            i += 1
            continue
        else:
            flush_table()

        # Blockquote
        if stripped.startswith('>'):
            flush_list()
            in_quote = True
            quote_lines.append(stripped)
            i += 1
            continue
        else:
            flush_quote()

        # Blank line
        if not stripped:
            flush_list()
            i += 1
            continue

        # Headings
        m = re.match(r'^(#{1,6})\s+(.*)$', stripped)
        if m:
            flush_list()
            level = len(m.group(1))
            title = m.group(2).strip()
            if level == 1:
                p = doc.add_paragraph()
                p.alignment = WD_ALIGN_PARAGRAPH.CENTER
                run = p.add_run(title)
                set_run_font(run, size=22, bold=True, color='2F5496')
                add_bottom_border(p, size=12, color='2F5496', space=4)
                # Version line if present in next lines
            elif level == 2:
                p = doc.add_heading(level=1)
                p.text = ''
                run = p.add_run(title)
                set_run_font(run, size=16, bold=True, color='2F5496')
                add_bottom_border(p, size=6, color='2F5496', space=2)
            elif level == 3:
                p = doc.add_heading(level=2)
                p.text = ''
                run = p.add_run(title)
                set_run_font(run, size=14, bold=True, color='2F5496')
            elif level == 4:
                p = doc.add_heading(level=3)
                p.text = ''
                run = p.add_run(title)
                set_run_font(run, size=12.5, bold=True, color='333333')
            elif level == 5:
                p = doc.add_heading(level=4)
                p.text = ''
                run = p.add_run(title)
                set_run_font(run, size=11.5, bold=True, color='444444')
            else:  # level 6
                p = doc.add_heading(level=5)
                p.text = ''
                run = p.add_run(title)
                set_run_font(run, size=11, bold=True, italic=True, color='555555')
            i += 1
            continue

        # Ordered list
        m = re.match(r'^(\s*)(\d+)\.\s+(.*)$', line)
        if m:
            indent = len(m.group(1)) // 2
            num = int(m.group(2))
            text = m.group(3)
            # Determine nesting level: match indent
            # Reset/trim stack
            while len(list_stack) > indent + 1:
                list_stack.pop()
            if len(list_stack) <= indent:
                list_stack.append(('ol', num))
            else:
                list_stack[indent] = ('ol', num)
            p = doc.add_paragraph()
            p.paragraph_format.left_indent = Inches(0.3 + indent * 0.25)
            prefix = f"{num}. "
            run = p.add_run(prefix)
            set_run_font(run, bold=True)
            parse_inline(text, p)
            # Increment counter in stack for next items at same level
            # (simplified: next numbered list will have correct numbers from md)
            i += 1
            continue

        # Unordered list
        m = re.match(r'^(\s*)([-*+])\s+(.*)$', line)
        if m:
            indent = len(m.group(1)) // 2
            text = m.group(3)
            while len(list_stack) > indent + 1:
                list_stack.pop()
            if len(list_stack) <= indent:
                list_stack.append(('ul', None))
            else:
                list_stack[indent] = ('ul', None)
            p = doc.add_paragraph()
            p.paragraph_format.left_indent = Inches(0.3 + indent * 0.25)
            bullet = '● ' if indent == 0 else ('○ ' if indent == 1 else '■ ')
            run = p.add_run(bullet)
            set_run_font(run, bold=True, color='2F5496')
            parse_inline(text, p)
            i += 1
            continue

        # Fallback: regular paragraph
        p = doc.add_paragraph()
        parse_inline(stripped, p)
        i += 1

    # Flush any remaining
    flush_code()
    flush_table()
    flush_quote()
    flush_list()

    doc.save(str(docx_path))
    print(f"DOCX saved: {docx_path}")


if __name__ == '__main__':
    if len(sys.argv) < 3:
        print("Usage: python md2docx.py <input.md> <output.docx>")
        sys.exit(1)
    md_path = Path(sys.argv[1])
    docx_path = Path(sys.argv[2])
    process_markdown(md_path, docx_path)
