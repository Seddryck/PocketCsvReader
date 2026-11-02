# Maintaining the documentation

Documentation sources live in `_docs`. Navigation is defined in `_data/navigation_docs.yml`; homepage cards in `navigation_boxes.yml`. Keep eight task-oriented groups. The overview is the common Docs entry point. Compatibility pages may carry `tags: [compatibility]` and remain linked from their replacements rather than appearing in the main learning path.

## One authoritative explanation

Use format guides for grammar and deviations, shared guides for ownership/conversion/outputs, and performance guides for optimization contracts. Preserve existing slugs; retain meaningful old fragment IDs when moving sections. Use relative published routes such as `../source-positions/`, never raw `/docs/` links that lose the site base path or Markdown source links. External repository references should be full GitHub links.

## Repeatable checks

From the repository root, with Python 3.11+ and the .NET 10 SDK:

```sh
python -m unittest discover -s .github/tools -p 'test_check_docs.py'
python .github/tools/check_docs.py
python .github/tools/check_docs.py --examples
```

Runnable examples are ordinary fenced `csharp` blocks preceded by `<!-- example: unique-name -->`; their exact expected console output is a fenced `text` block preceded by `<!-- output: unique-name -->`. They must be complete top-level console programs with explicit format namespaces and in-memory input. The checker extracts, compiles, and executes each example in Release, comparing output exactly. Generated projects/logs live under ignored `artifacts/docs-examples`; no C# copy of a documentation example is maintained. Unmarked fragments are explanatory API excerpts and are not claimed to compile independently.

## Render the site

Use Ruby 3.3 and Bundler 2.3.18 with the committed Gemfile.lock. From `docs`:

```sh
bundle install
bundle exec jekyll build --strict_front_matter --destination ../artifacts/docs-site
```

Then, from the repository root:

```sh
python .github/tools/check_docs.py --site artifacts/docs-site
```

The rendered checker resolves local links and fragments under `/PocketCsvReader`, including desktop/mobile navigation. Rendered tables must contain actual header cells; table scrolling is handled by CSS without rewriting generated markup. The /docs/ compatibility landing page links to the overview without forcing a timed redirect. Use `--baseurl ''` when checking an intentionally root-mounted build. Ruby dependencies, build output, example executables, and logs must remain outside tracked site sources.

The Documentation workflow runs these checks once on Ubuntu/.NET 10/Ruby 3.3 and retains a rendered preview artifact; it does not publish. Library TFMs and release workflows remain independent. Review desktop and mobile navigation, first-example tables/code, long reference pages, old URLs/fragments, and selected format pages visually before merging. Run `git diff --check` and report exact focused tests/builds and any checks not run.

## CI decision record

| Dimension | Evidence | Decision |
| --- | --- | --- |
| Deliverable | Static Jekyll HTML, no executable distribution | Validate and retain a preview; no publication |
| Framework | Documentation snippets are invariant; library targets remain 8/9/10 | Run documentation examples once on .NET 10; leave library matrix unchanged |
| OS/RID | No new native library or application output | One Ubuntu runner; no RID/architecture matrix |
| Dependencies | Existing Gemfile/lock plus Python stdlib and .NET SDK | Ruby 3.3/Bundler 2.3.18, locked gems, no Python packages |
| Permissions | Read source and upload workflow artifact | contents: read; no deployment credentials |

After merge and deployment, check the live entry point, legacy links, mobile menu, and representative examples. A PR preview does not establish that production has been deployed.
