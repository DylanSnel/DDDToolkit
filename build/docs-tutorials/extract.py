"""Writes every file a tutorial page shows whole into a folder, so the folder can be built as the reader's solution.

A file is a fenced csharp or xml block below a paragraph that starts by naming it in bold, **`path`**, after one
blank line.
A later block for the same path replaces an earlier one: a step shows a file whole again when it changes it. A path
that starts with Harbor.<Module>. is a project of that module, in src/Modules/<Module>/. The toolkit's version in
Directory.Packages.props is replaced by the one being verified.

Usage: extract.py <out folder> <toolkit version> <page.md> [<page.md> ...], the pages in reading order.
"""
import os
import re
import sys

FILE = re.compile(r'^\*\*`([^`]+\.(?:cs|csproj|props))`\*\*[^\n]*(?:\n(?!```)[^\n]+)*\n\n```(?:csharp|xml)[^\n]*\n(.*?)\n```', re.S | re.M)
DOCUMENTED_VERSION = re.compile(r'Version="3\.\d+\.\d+(?:-[0-9A-Za-z.]+)?"')

# The Tenancy tutorial shows TenantId.cs and says the other three ids are the same file under another name.
SAME_AS_TENANT_ID = ['SeatId', 'OrganizationUnitId', 'RoleId']


def place(path):
    first = path.split('/')[0].split('.')
    if len(first) >= 3 and first[0] == 'Harbor' and first[1] != 'Host':
        return f'src/Modules/{first[1]}/{path}'
    return path


def main(out, version, pages):
    files = {}
    for page in pages:
        with open(page, encoding='utf-8') as reader:
            for match in FILE.finditer(reader.read()):
                files[place(match.group(1))] = match.group(2) + '\n'

    tenant_id = 'src/Modules/Tenancy/Harbor.Tenancy.Contracts/ValueObjects/TenantId.cs'
    if tenant_id in files:
        for name in SAME_AS_TENANT_ID:
            files[tenant_id.replace('TenantId', name)] = files[tenant_id].replace('TenantId', name)

    for path, body in files.items():
        if path == 'Directory.Packages.props':
            # Only the toolkit's packages: their ids start with Temp.DDDToolkit.
            body = '\n'.join(
                DOCUMENTED_VERSION.sub(f'Version="{version}"', line) if 'Temp.DDDToolkit' in line else line
                for line in body.split('\n'))
        target = os.path.join(out, path)
        os.makedirs(os.path.dirname(target) or out, exist_ok=True)
        with open(target, 'w', encoding='utf-8', newline='\n') as writer:
            writer.write(body)
        print(path)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2], sys.argv[3:])
