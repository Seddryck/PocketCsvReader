"""Validate documentation sources, rendered links, and marked C# console examples.

Run with Python 3.11+; no third-party Python dependencies are required.
"""
import argparse
from html.parser import HTMLParser
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import unquote, urljoin, urlsplit


def frontmatter(text):
    if not text.startswith('---\n'):
        return {}, text
    _, header, body = text.split('---', 2)
    fields = {}
    for line in header.splitlines():
        key, separator, value = line.partition(':')
        if separator and re.fullmatch(r'\w+', key):
            fields[key] = value.strip().strip('"\'')
    return fields, body


def check_navigation(root, pages, errors):
    nav = (root / 'docs/_data/navigation_docs.yml').read_text(encoding='utf-8')
    slugs = re.findall(r'^ {2}- ([\w-]+)\s*$', nav, re.M)
    if len(slugs) != len(set(slugs)):
        errors.append('Duplicate documentation navigation entries')
    groups = re.findall(r'^- title: (.+)$', nav, re.M)
    if len(groups) != 8:
        errors.append('Expected eight task-oriented navigation groups')
    expected_groups = ['Getting started', 'Format guides', 'Inspect and prepare input',
                       'Reading data', 'Configuration', 'Validation and diagnostics',
                       'Performance', 'Integrations']
    if groups != expected_groups:
        errors.append('Navigation groups/order differ from the documented organization')
    inspect_group = nav.split('- title: Inspect and prepare input', 1)[-1].split('- title:', 1)[0]
    if '  - schema-inference' not in inspect_group:
        errors.append('Inference must remain discoverable under input inspection')
    for slug in slugs:
        if not (pages / f'{slug}.md').is_file():
            errors.append(f'Navigation target missing: {slug}')
    return slugs


def check_page(page, fields, body, slugs, errors):
    if not fields.get('title'):
        errors.append(f'{page.name}: missing title')
    if page.stem not in slugs and 'compatibility' not in fields.get('tags', ''):
        errors.append(f'{page.name}: missing from navigation')
    if len(re.findall(r'^```', body, re.M)) % 2:
        errors.append(f'{page.name}: unbalanced code fences')
    if re.search(r'\]\(/docs/', body):
        errors.append(f'{page.name}: root-relative documentation link bypasses baseurl')


def collect_examples(page, body, marked, errors):
    for match in re.finditer(r'<!-- example: ([\w-]+) -->\s*```csharp\n(.*?)```', body, re.S):
        if match[1] in marked:
            errors.append(f'Duplicate example marker: {match[1]}')
        expected = re.search(r'<!-- output: ' + re.escape(match[1]) + r' -->\s*```text\n(.*?)```', body, re.S)
        if expected is None:
            errors.append(f'{page.name}: {match[1]} needs an expected-output block')
        marked[match[1]] = (page.name, match[2], expected[1].strip() if expected else '')


def check_source_links(page, fields, body, routes, errors):
    source = fields.get('permalink', f'/docs/{page.stem}/')
    for match in re.finditer(r'\]\(([^\s)]+)(?:\s+"[^"]*")?\)', body):
        target = match[1].strip('<>')
        parts = urlsplit(target)
        if parts.scheme or parts.netloc:
            continue
        if urlsplit(urljoin(source, target)).path not in routes:
            errors.append(f'{page.name}: unresolved local documentation link {target}')


def check_entrypoints(root, slugs, errors):
    for name in ['navigation_header.yml', 'navigation_mobile.yml', 'navigation_footer.yml']:
        text = (root / 'docs/_data' / name).read_text(encoding='utf-8')
        if not re.search(r'url: /docs/why-pocketcsvreader/\s*$', text, re.M):
            errors.append(f'{name}: Docs must point to the overview')
    boxes = (root / 'docs/_data/navigation_boxes.yml').read_text(encoding='utf-8')
    for target in re.findall(r'^ {2}doc: (.+)$', boxes, re.M):
        if target not in slugs:
            errors.append(f'Homepage card points outside documentation navigation: {target}')


def source_check(root):
    errors = []
    pages = root / 'docs/_docs'
    slugs = check_navigation(root, pages, errors)
    routes = {'/docs/', '/contact/', '/changelog-timeline/'}
    marked = {}
    sources = []
    for page in sorted(pages.glob('*.md')):
        fields, body = frontmatter(page.read_text(encoding='utf-8'))
        sources.append((page, fields, body))
        check_page(page, fields, body, slugs, errors)
        routes.add(fields.get('permalink', f'/docs/{page.stem}/'))
        collect_examples(page, body, marked, errors)
    for page, fields, body in sources:
        check_source_links(page, fields, body, routes, errors)
    check_entrypoints(root, slugs, errors)
    print(f'Source checks: {len(slugs)} navigation entries, {len(list(pages.glob("*.md")))} pages, {len(marked)} runnable examples')
    if errors:
        raise ValueError('\n'.join(errors))
    return marked


class Links(HTMLParser):
    def __init__(self):
        super().__init__()
        self.ids = set()
        self.links = []
        self.table_headers = []
        self._open_tables = []

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'table':
            self._open_tables.append(0)
        if tag == 'th' and self._open_tables:
            self._open_tables[-1] += 1
        if attrs.get('id'):
            self.ids.add(attrs['id'])
        if tag == 'a' and 'href' in attrs:
            self.links.append(attrs['href'])

    def handle_endtag(self, tag):
        if tag == 'table' and self._open_tables:
            self.table_headers.append(self._open_tables.pop())


def read_rendered_documents(site):
    documents = {}
    for page in sorted(site.rglob('*.html')):
        parser = Links()
        parser.feed(page.read_text(encoding='utf-8'))
        if 0 in parser.table_headers:
            raise ValueError(f'{page}: rendered table has no header cells')
        documents[page.resolve()] = parser
    if not documents:
        raise ValueError(f'No rendered HTML documents found in {site}')
    return documents


def check_rendered_link(href, route, relative, site, baseurl, documents, errors):
    parsed = urlsplit(href)
    if parsed.scheme or parsed.netloc:
        return
    if not href:
        errors.append(f'{relative}: empty link (possible unresolved template target)')
        return
    target = urlsplit(urljoin(baseurl.rstrip('/') + route, href))
    path = unquote(target.path)
    if baseurl and not (path == baseurl or path.startswith(baseurl + '/')):
        errors.append(f'{relative}: link bypasses baseurl: {href}')
        return
    path = path[len(baseurl):].lstrip('/')
    candidate = site / path
    if target.path.endswith('/') or candidate.is_dir():
        candidate = candidate / 'index.html'
    if not candidate.exists():
        errors.append(f'{relative}: missing target {href}')
    elif target.fragment and candidate.resolve() in documents:
        if unquote(target.fragment) not in documents[candidate.resolve()].ids:
            errors.append(f'{relative}: missing anchor {href}')


def site_check(site, baseurl):
    if not site.is_dir():
        raise ValueError(f'Rendered site does not exist: {site}')
    documents = read_rendered_documents(site)
    errors = []
    for page, parser in documents.items():
        relative = page.relative_to(site.resolve()).as_posix()
        route = '/' + (relative[:-10] if relative.endswith('index.html') else relative)
        for href in parser.links:
            check_rendered_link(href, route, relative, site, baseurl, documents, errors)
    if errors:
        raise ValueError('\n'.join(sorted(set(errors))))
    print(f'Rendered checks: {len(documents)} HTML pages; internal routes and anchors resolve under {baseurl or "/"}')


def examples_check(root, examples):
    work = root / 'artifacts/docs-examples'
    work.mkdir(parents=True, exist_ok=True)
    projects = ['PocketCsvReader', 'PocketCsvReader.Json', 'PocketCsvReader.Ndjson',
                'PocketCsvReader.FixedWidth', 'PocketCsvReader.KeyValue',
                'PocketCsvReader.WebLogs', 'PocketCsvReader.SecurityLogs',
                'PocketCsvReader.Arrow', 'PocketCsvReader.Chrononuensis']
    references = '\n'.join(f'<ProjectReference Include="{(root / p / (p + ".csproj")).as_posix()}" />' for p in projects)
    (work / 'DocumentationExamples.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFrameworks>net10.0</TargetFrameworks>
    <IsPackable>false</IsPackable><GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>{references}</ItemGroup>
</Project>\n''', encoding='utf-8')
    for name, (page, code, expected) in examples.items():
        (work / 'Program.cs').write_text(code, encoding='utf-8')
        build = subprocess.run(['dotnet', 'build', str(work / 'DocumentationExamples.csproj'), '-c', 'Release', '-f', 'net10.0', '--nologo', '--verbosity', 'quiet'], cwd=root, capture_output=True, text=True, encoding='utf-8')
        (work / f'{name}.build.log').write_text(build.stdout + build.stderr, encoding='utf-8')
        if build.returncode:
            raise ValueError(f'{page}: {name} failed to compile\n{build.stdout}\n{build.stderr}')
        run = subprocess.run(['dotnet', str(work / 'bin/Release/net10.0/DocumentationExamples.dll')], cwd=root, capture_output=True, text=True, encoding='utf-8')
        if run.returncode or run.stdout.strip() != expected:
            raise ValueError(f'{page}: {name}: expected {expected!r}; got {run.stdout!r}\n{run.stderr}')
        print(f'PASS {name} ({page})', flush=True)
    print(f'{len(examples)} compiled examples matched expected output on .NET 10 Release')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--site', type=Path)
    parser.add_argument('--baseurl', default='/PocketCsvReader')
    parser.add_argument('--examples', action='store_true')
    args = parser.parse_args()
    root = args.root.resolve()
    try:
        examples = source_check(root)
        if args.examples:
            examples_check(root, examples)
        if args.site:
            site_check(args.site.resolve(), args.baseurl.rstrip('/'))
    except (ValueError, OSError) as error:
        print(error, file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
