# CCP PDF Import Smoke Test

This small Windows x64 test host exercises the real `NAPS2.Pdf.PdfImporter` against the files installed by the CCP Scan setup package.

The test creates a dependency-free three-page PDF, points `NAPS2_TEST_DEPS` at the installed CCP Scan directory, imports the PDF through `PdfImporter`, and fails unless exactly three pages are returned.

It is executed automatically by `.github/workflows/ccp-windows-setup.yml` after the Win64 installer has been built and installed silently. The tested setup artifact is uploaded only if build, native Pdfium loading, silent install, Vietnamese/default-icon/desktop-shortcut checks, real PDF import, GUI startup, and silent uninstall all pass.
