"""Regression checks for source parsing and actual rendered table accessibility."""
from pathlib import Path
import tempfile
import unittest

from check_docs import frontmatter, read_rendered_documents


class FrontmatterTests(unittest.TestCase):
    def test_values_keep_embedded_colons_and_ignore_indented_keys(self):
        fields, body = frontmatter('---\ntitle: "Input: CSV"\n  nested: ignored\n---\nBody')
        self.assertEqual(fields, {'title': 'Input: CSV'})
        self.assertEqual(body, '\nBody')

    def test_long_whitespace_and_values_preserve_field_content(self):
        value = 'x' * 100_000
        fields, _ = frontmatter('---\ntitle:' + ' ' * 100_000 + value + ' ' * 100_000 + '\n---\n')
        self.assertEqual(fields['title'], value)

    def test_unquoted_empty_and_unicode_values(self):
        fields, _ = frontmatter('---\ntitle: données\nsubtitle:   \n---\n')
        self.assertEqual(fields, {'title': 'données', 'subtitle': ''})

    def test_content_without_frontmatter_is_unchanged(self):
        self.assertEqual(frontmatter('Plain text'), ({}, 'Plain text'))


class RenderedTablesTests(unittest.TestCase):
    def check_page(self, markup):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / 'index.html').write_text(markup, encoding='utf-8')
            return read_rendered_documents(path)

    def test_real_header_cells_pass_even_when_code_mentions_table_markup(self):
        documents = self.check_page('<pre>&lt;table&gt;</pre><table><thead><tr><th>API</th></tr></thead><tbody><tr><td>Read</td></tr></tbody></table>')
        self.assertEqual(next(iter(documents.values())).table_headers, [1])

    def test_headerless_rendered_table_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'no header cells'):
            self.check_page('<table><tr><td>Read</td></tr></table>')

    def test_each_table_requires_its_own_headers(self):
        with self.assertRaisesRegex(ValueError, 'no header cells'):
            self.check_page('<table><tr><th>Good</th></tr></table><table><tr><td>Missing</td></tr></table>')

    def test_pages_without_tables_are_valid(self):
        self.assertEqual(next(iter(self.check_page('<h1>Guide</h1>').values())).table_headers, [])


if __name__ == '__main__':
    unittest.main()
