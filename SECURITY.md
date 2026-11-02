# Security Policy

This policy applies to PocketCsvReader and its extension packages.

## Supported Versions

Security fixes are provided for the latest stable release of each package. Upgrade
an affected package to its latest stable release before checking whether a
vulnerability is still present.

| Release | Security updates |
| ------- | ---------------- |
| Latest stable release of each package | Supported |
| Earlier releases and prereleases | Not supported |

Security fixes are released in a new stable version. Fixes are not backported to
earlier releases. If an issue prevents upgrading, include that information in your
private report.

## Reporting a Vulnerability

Report suspected vulnerabilities through
[GitHub's private vulnerability reporting form](https://github.com/Seddryck/PocketCsvReader/security/advisories/new).
You must sign in to GitHub to submit a report. Reports are shared privately with
the repository maintainers.

Do not disclose vulnerability details in public issues, discussions, or pull
requests before coordinating disclosure with the maintainers. Use public issues
for ordinary bugs that have no security impact.

Include the following information when available:

- The affected PocketCsvReader package and version, .NET version, and operating system.
- A description of the vulnerability and its potential security impact.
- Steps to reproduce, including a minimal input file or proof of concept.
- Relevant configuration, source file paths, and error messages.
- Any suggested mitigation or fix.

Remove credentials, personal data, and other sensitive information from examples
and attachments. You do not need a complete diagnosis to report a suspected issue.

## Response and Disclosure

Maintainers review reports on a best-effort basis. There is no guaranteed response
or resolution time. Use the private report to provide additional information and
coordinate a fix and any public disclosure with the maintainers.
