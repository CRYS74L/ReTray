# Selected settings icons

Selected by the user: Ionicons `power-outline` from option P3 for startup, with the three Phosphor Light glyphs from option C1.

- startup.svg: https://github.com/ionic-team/ionicons/blob/main/src/svg/power-outline.svg
- theme.svg: https://github.com/phosphor-icons/core/blob/main/assets/light/sun-light.svg
- language.svg: https://github.com/phosphor-icons/core/blob/main/assets/light/translate-light.svg
- configuration.svg: https://github.com/phosphor-icons/core/blob/main/assets/light/file-code-light.svg

The checked-in SVG paths are the selected library assets. Ionicons uses an open stroked arc and a separate vertical line, with inline styles and round caps; Phosphor Light uses filled outline paths. The renderer supports both and normalizes the visible ink to the existing icon size, compensates line width to the settings page's one DIP outline, then centers the final ink in the unchanged 28 DIP icon column. Geometry, not font codepoints, supplies the shapes. Color follows the active text brush.

LICENSE includes the full Ionicons MIT notice and the Phosphor MIT notice. Both LICENSE and this notice are shipped with the application. Only these four glyphs change; settings controls, list dividers, popup layout and scrolling behavior are retained.
