# Design

The two-arrow icon is generated from `assets/sync.svg`: enlarged graphite hexagonal ring with a light center and simple orange upload/download arrows. There is no white outer tile; surroundings are transparent. Embedded multi-size ICO resources supply the static executable/window/taskbar icon; 12 cached arrow-rotation frames use the same transparent design in the tray and window during transfers. WPF software rendering and virtualized lazy file rows avoid render-thread and large-list mode-switch stalls. Send/Download share one form; only relevant controls are visible.

Existing WPF interface retained: Segoe UI, neutral light background, white cards, orange main action and active stage, standard Windows frame. Default 1080×1040 logical pixels, limited to screen work area. Main content and sidebar keep their original workflow; progress/action/result are pinned.

Visible vertical scrollbars have a shared 12px content gutter; hidden bars reserve no extra gap. Table headers follow the virtualized row viewport so size/status columns remain aligned after folder or mode changes. Native WPF scrollbars and keyboard scrolling are retained.

Offline guides use the same approved icon and orange/graphite palette. Public images are real WPF demonstration renders with artificial files, fictional account/project and neutral `C:\Projects\Demo` paths; no real credentials or upload content. Density renders at 100/125/150/200% are not physical Windows-DPI acceptance.

Documentation layout follows the owner's public HotspotControl/Kaktus pattern: clear purpose, platform/status, language links, first steps, synthetic screenshots, requirements/limitations, checksums, privacy/build/rights. No external app assets/code were copied, and this preparation does not publish a GitHub Pages site.
