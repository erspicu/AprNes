#!/usr/bin/env python3
"""
send_mail.py - Send email via Gmail SMTP (App Password)

Usage:
    python tools/send_mail.py --to EMAIL --subject "SUBJECT" --body "TEXT"
    python tools/send_mail.py --to EMAIL --subject "SUBJECT" --file body.md
    python tools/send_mail.py --to EMAIL --subject "SUBJECT" --file body.md --html
    python tools/send_mail.py --to EMAIL --subject "SUBJECT" --body "TEXT" --attach file1.png file2.zip
    echo "body text" | python tools/send_mail.py --to EMAIL --subject "SUBJECT" --stdin

Options:
    --to        Recipient email (default: baxermux@gmail.com)
    --subject   Email subject line
    --body      Inline body text
    --file      Read body from file (supports .md, .txt, .html)
    --html      Convert markdown to HTML email
    --stdin     Read body from stdin
    --attach    Attach one or more files (images, zips, etc.)
    --from      Sender email (default: baxermux@gmail.com)
    --dry-run   Print email without sending

Password resolution order (first hit wins):
    1. --password CLI arg
    2. File at C:\\key\\gmail.txt (stripped)
    3. $GMAIL_APP_PASSWORD env var
"""

import argparse
import smtplib
import sys
import os
import mimetypes
from email.mime.text import MIMEText
from email.mime.multipart import MIMEMultipart
from email.mime.base import MIMEBase
from email.mime.image import MIMEImage
from email import encoders

DEFAULT_FROM = "baxermux@gmail.com"
DEFAULT_TO = "baxermux@gmail.com"
PASSWORD_FILE = r"C:\key\gmail.txt"


def load_password():
    """Read password from PASSWORD_FILE, falling back to env var."""
    try:
        with open(PASSWORD_FILE, 'r', encoding='utf-8') as f:
            pw = f.read().strip()
            if pw:
                return pw
    except FileNotFoundError:
        pass
    except OSError as e:
        print(f"Warning: could not read {PASSWORD_FILE}: {e}", file=sys.stderr)
    return os.environ.get('GMAIL_APP_PASSWORD', '')


DEFAULT_PASSWORD = load_password()

def markdown_to_html(md_text):
    """Simple markdown to HTML conversion (no external deps)."""
    import re
    html = md_text
    html = re.sub(r'```(\w*)\n(.*?)```', lambda m: f'<pre style="background:#1e1e1e;color:#d4d4d4;padding:12px;border-radius:6px;overflow-x:auto;font-size:13px"><code>{m.group(2).replace("<","&lt;").replace(">","&gt;")}</code></pre>', html, flags=re.DOTALL)
    html = re.sub(r'`([^`]+)`', r'<code style="background:#2d2d2d;color:#e6db74;padding:2px 6px;border-radius:3px;font-size:13px">\1</code>', html)
    html = re.sub(r'^### (.+)$', r'<h3 style="color:#4fc3f7;margin:18px 0 8px">\1</h3>', html, flags=re.MULTILINE)
    html = re.sub(r'^## (.+)$', r'<h2 style="color:#81c784;border-bottom:1px solid #444;padding-bottom:4px;margin:24px 0 12px">\1</h2>', html, flags=re.MULTILINE)
    html = re.sub(r'^# (.+)$', r'<h1 style="color:#fff;margin-bottom:16px">\1</h1>', html, flags=re.MULTILINE)
    html = re.sub(r'\*\*(.+?)\*\*', r'<strong>\1</strong>', html)
    html = re.sub(r'\*(.+?)\*', r'<em>\1</em>', html)
    html = re.sub(r'^> (.+)$', r'<blockquote style="border-left:3px solid #666;margin:8px 0;padding:4px 12px;color:#aaa">\1</blockquote>', html, flags=re.MULTILINE)
    def convert_table(match):
        lines = match.group(0).strip().split('\n')
        rows = [l for l in lines if not __import__('re').match(r'^\|[-:\s|]+\|$', l)]
        out = '<table style="border-collapse:collapse;margin:12px 0;font-size:14px">'
        for i, row in enumerate(rows):
            cells = [c.strip() for c in row.strip('|').split('|')]
            tag = 'th' if i == 0 else 'td'
            style = 'style="border:1px solid #555;padding:6px 12px;text-align:left"'
            bg = ' style="background:#2a2a2a"' if i == 0 else ''
            out += f'<tr{bg}>'
            for c in cells:
                out += f'<{tag} {style}>{c}</{tag}>'
            out += '</tr>'
        out += '</table>'
        return out
    html = re.sub(r'(\|.+\|(?:\n\|.+\|)+)', convert_table, html)
    html = re.sub(r'^- (.+)$', r'<li style="margin:2px 0">\1</li>', html, flags=re.MULTILINE)
    html = re.sub(r'(<li.*</li>\n?)+', lambda m: f'<ul style="margin:8px 0;padding-left:24px">{m.group(0)}</ul>', html)
    html = re.sub(r'\n\n+', '</p><p style="margin:8px 0;line-height:1.6">', html)
    html = f'''<div style="font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#1a1a1a;color:#e0e0e0;padding:24px 32px;max-width:800px;margin:0 auto;border-radius:8px">
<p style="margin:8px 0;line-height:1.6">{html}</p>
</div>'''
    return html


def attach_file(msg, filepath):
    """Attach a file to a MIMEMultipart message."""
    filename = os.path.basename(filepath)
    mime_type, _ = mimetypes.guess_type(filepath)
    if mime_type is None:
        mime_type = 'application/octet-stream'

    maintype, subtype = mime_type.split('/', 1)

    with open(filepath, 'rb') as f:
        data = f.read()

    if maintype == 'image':
        part = MIMEImage(data, _subtype=subtype)
    else:
        part = MIMEBase(maintype, subtype)
        part.set_payload(data)
        encoders.encode_base64(part)

    part.add_header('Content-Disposition', 'attachment', filename=filename)
    msg.attach(part)


def send_email(from_addr, to_addr, subject, body, content_type='plain',
               password=None, dry_run=False, attachments=None):
    password = password or DEFAULT_PASSWORD or os.environ.get('GMAIL_APP_PASSWORD', '')
    if not password and not dry_run:
        raise RuntimeError(
            f"Gmail app password not found. Place it in {PASSWORD_FILE} "
            f"or set GMAIL_APP_PASSWORD env var, or pass --password.")

    msg = MIMEMultipart('mixed')
    msg['From'] = from_addr
    msg['To'] = to_addr
    msg['Subject'] = subject

    msg.attach(MIMEText(body, content_type, 'utf-8'))

    if attachments:
        for filepath in attachments:
            if os.path.isfile(filepath):
                attach_file(msg, filepath)
                if not dry_run:
                    print(f"  Attached: {os.path.basename(filepath)}")
            else:
                print(f"  Warning: attachment not found: {filepath}")

    if dry_run:
        print(f"=== DRY RUN ===")
        print(f"From: {from_addr}")
        print(f"To: {to_addr}")
        print(f"Subject: {subject}")
        print(f"Content-Type: text/{content_type}")
        print(f"Attachments: {len(attachments) if attachments else 0}")
        print(f"Body ({len(body)} chars):")
        print(body[:500])
        if len(body) > 500:
            print(f"... ({len(body) - 500} more chars)")
        return

    with smtplib.SMTP_SSL('smtp.gmail.com', 465) as server:
        server.login(from_addr, password)
        server.send_message(msg)

    print(f"Sent to {to_addr}: {subject}")


def main():
    parser = argparse.ArgumentParser(description='Send email via Gmail SMTP')
    parser.add_argument('--to', default=DEFAULT_TO, help='Recipient email')
    parser.add_argument('--from', dest='from_addr', default=DEFAULT_FROM, help='Sender email')
    parser.add_argument('--subject', '-s', required=True, help='Subject line')
    parser.add_argument('--body', '-b', help='Inline body text')
    parser.add_argument('--file', '-f', help='Read body from file')
    parser.add_argument('--stdin', action='store_true', help='Read body from stdin')
    parser.add_argument('--html', action='store_true', help='Convert markdown to HTML')
    parser.add_argument('--attach', '-a', nargs='+', help='Attach files (images, zips, etc.)')
    parser.add_argument('--password', '-p', help=f'Override password (default: read {PASSWORD_FILE})')
    parser.add_argument('--dry-run', action='store_true', help='Print without sending')
    args = parser.parse_args()

    # Get body
    if args.file:
        with open(args.file, 'r', encoding='utf-8') as f:
            body = f.read()
    elif args.stdin:
        body = sys.stdin.read()
    elif args.body:
        body = args.body
    else:
        body = ''  # allow attachment-only emails

    # Content type
    content_type = 'plain'
    if args.html:
        body = markdown_to_html(body)
        content_type = 'html'
    elif args.file and args.file.endswith('.html'):
        content_type = 'html'

    send_email(args.from_addr, args.to, args.subject, body, content_type,
               password=args.password,
               dry_run=args.dry_run, attachments=args.attach)


if __name__ == '__main__':
    main()
