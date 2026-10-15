Window controls adapted from react-desktop (MIT).
https://github.com/gabrielbull/react-desktop/tree/bba61374849dec896a9eea321cf4e3f02b3fd4c6/src/TitleBar/macOs/Controls

Ported the 12px circles, 20px pitch, 10px SVG symbols, palette, pressed and unfocused states and group-hover behavior to WPF. Order is intentionally minimize / maximize / close for Windows. This is not an Apple native control; it does not invoke AppKit on Windows. The upstream control uses direct state changes, not tween/scale animations.
