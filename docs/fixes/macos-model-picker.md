# macOS model picker repair (0.2.4 candidate)

The model picker now uses a viewport-fitted overlay, recalculated after the asynchronous model catalog changes its size and on scrolling/resizing. Connection testing discovers an available model if no ID has been selected; incomplete selection is reported separately from authentication errors.

Validation: Node model-picker and overlay regression checks; universal DMG build; real WKWebView click/open/catalog/selection checks, full viewport bounds and hit-testing; launch from the mounted DMG on macos-26-intel and xcode-27. API catalogs were synthetic fixtures and no personal credentials were used.

Verified run: https://github.com/GamoffVad/Sputnik/actions/runs/45032521960
Artifact: Sputnik-macOS-0.2.4-Universal
DMG SHA-256: bcf8f3d8539a9dc1b87dbf7ad7378bb00e103dde7fc2b56323f17e35a521e8c5

This is a build artifact, not a published release. Existing signing policy remains ad hoc.
