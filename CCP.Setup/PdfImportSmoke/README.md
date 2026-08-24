# CCP PDF Import Smoke Test

This small Windows x64 test host exercises the real `NAPS2.Pdf.PdfImporter` against the files installed by the CCP Scan setup package.

The test creates a dependency-free three-page PDF, points `NAPS2_TEST_DEPS` at the installed CCP Scan directory, imports the PDF through `PdfImporter`, and fails unless exactly three pages are returned.

It is executed automatically by `.github/workflows/ccp-windows-setup.yml` after the Win64 installer has been built and installed silently.

The workflow validates:

- Windows x64 Release build;
- Win64 Inno Setup generation;
- packaged Pdfium x64 assets and `LoadLibraryExW` loading;
- silent installation into an isolated test directory;
- Vietnamese default configuration;
- CCP icon and Desktop shortcut;
- real `PdfImporter` import of a generated three-page PDF;
- installed GUI application startup;
- silent uninstall and cleanup.

Only after all validation steps, including uninstall, succeed does the workflow upload an artifact whose name ends in `-TESTED`. Failures upload diagnostics instead.

Physical TWAIN/WIA scanning is intentionally not automated on GitHub-hosted runners because they do not have the user's scanner hardware. That remains the final manual acceptance test on a real Windows PC with the target scanner attached.
