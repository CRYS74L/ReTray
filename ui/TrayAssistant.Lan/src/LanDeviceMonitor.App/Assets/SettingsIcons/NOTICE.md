# Settings icons

These four monochrome SVGs are from Microsoft Fluent System Icons (MIT):
https://github.com/microsoft/fluentui-system-icons

Pinned upstream commit: a347af95478f989038cccbf085978865264c048a

- language.svg: assets/Local Language Globe/SVG/ic_fluent_local_language_globe_20_regular.svg
- theme.svg: assets/Paint Brush/SVG/ic_fluent_paint_brush_20_regular.svg
- startup.svg: assets/Task List Square Settings/SVG/ic_fluent_task_list_square_settings_20_regular.svg
- configuration.svg: assets/Document Settings/SVG/ic_fluent_document_settings_20_regular.svg

The language, theme and startup glyphs match the Lyricify Fusion screenshot supplied for this task. The publicly available WXRIW/Lyricify-App repository contains documentation, translations and app logos, but does not contain these settings glyphs. The official Fluent SVGs are used directly, without extracting or redistributing Lyricify application resources. The configuration glyph is selected from the same icon family.

The checked-in SVG source paths are unchanged. The WPF renderer normalizes each glyph and compensates the enlarged outline back to one DIP, then centers the resulting ink bounds. WPF paints it with the active theme's text brush and centers it in the same 28 DIP icon column used by the system settings rows. See LICENSE for the full upstream notice.
