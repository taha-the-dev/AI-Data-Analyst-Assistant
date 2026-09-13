---
name: Precision Analytics
colors:
  surface: '#f7f9fb'
  surface-dim: '#d8dadc'
  surface-bright: '#f7f9fb'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f2f4f6'
  surface-container: '#eceef0'
  surface-container-high: '#e6e8ea'
  surface-container-highest: '#e0e3e5'
  on-surface: '#191c1e'
  on-surface-variant: '#434655'
  inverse-surface: '#2d3133'
  inverse-on-surface: '#eff1f3'
  outline: '#737686'
  outline-variant: '#c3c6d7'
  surface-tint: '#0053db'
  primary: '#004ac6'
  on-primary: '#ffffff'
  primary-container: '#2563eb'
  on-primary-container: '#eeefff'
  inverse-primary: '#b4c5ff'
  secondary: '#585e6f'
  on-secondary: '#ffffff'
  secondary-container: '#d9dff4'
  on-secondary-container: '#5c6274'
  tertiary: '#4e565d'
  on-tertiary: '#ffffff'
  tertiary-container: '#676e76'
  on-tertiary-container: '#eaf1fa'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#dbe1ff'
  primary-fixed-dim: '#b4c5ff'
  on-primary-fixed: '#00174b'
  on-primary-fixed-variant: '#003ea8'
  secondary-fixed: '#dce2f6'
  secondary-fixed-dim: '#c0c6da'
  on-secondary-fixed: '#151b2a'
  on-secondary-fixed-variant: '#404757'
  tertiary-fixed: '#dce3ec'
  tertiary-fixed-dim: '#c0c7d0'
  on-tertiary-fixed: '#151c23'
  on-tertiary-fixed-variant: '#40484f'
  background: '#f7f9fb'
  on-background: '#191c1e'
  surface-variant: '#e0e3e5'
typography:
  headline-lg:
    fontFamily: Inter
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
    letterSpacing: -0.02em
  headline-md:
    fontFamily: Inter
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 28px
    letterSpacing: -0.01em
  headline-sm:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '600'
    lineHeight: 24px
  body-lg:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  body-md:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  body-sm:
    fontFamily: Inter
    fontSize: 13px
    fontWeight: '400'
    lineHeight: 18px
  label-md:
    fontFamily: Inter
    fontSize: 12px
    fontWeight: '500'
    lineHeight: 16px
    letterSpacing: 0.01em
  label-sm:
    fontFamily: Inter
    fontSize: 11px
    fontWeight: '600'
    lineHeight: 14px
    letterSpacing: 0.03em
  mono-md:
    fontFamily: jetbrainsMono
    fontSize: 13px
    fontWeight: '400'
    lineHeight: 20px
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  space-xs: 4px
  space-sm: 8px
  space-md: 12px
  space-lg: 16px
  space-xl: 24px
  container-margin: 24px
  gutter: 16px
---

## Brand & Style

The brand personality is professional, analytical, and authoritative. It is designed for data scientists and business analysts who require a high-density, low-friction interface to process complex information quickly. 

The design style is **Corporate / Modern** with a focus on **Minimalism**. It prioritizes utility and clarity over decorative elements. The aesthetic is defined by a "technical-premium" feel: crisp lines, a disciplined color palette, and a focus on structural alignment. The UI should evoke a sense of calm efficiency and absolute accuracy.

## Colors

The palette is anchored by **Primary Navy (#0B1220)** for high-contrast structural elements like sidebars and headers, providing a "command center" feel. **Primary Blue (#2563EB)** is reserved strictly for primary actions, progress indicators, and interactive states to ensure clear affordance.

- **Background (#F8FAFC)**: Used for the main application canvas to reduce eye strain.
- **Surface (#FFFFFF)**: Used for data cards, panels, and modals to create clear separation from the background.
- **Light Blue (#EFF6FF)**: A subtle tint for hover states, selected list items, and soft highlights.
- **Borders (#E5E7EB)**: The primary method of defining structure, replacing heavy shadows.

## Typography

This design system uses **Inter** for its exceptional legibility at small sizes and its neutral, systematic character. For technical data points, code snippets, or raw coordinates, **JetBrains Mono** is introduced to provide a clear distinction from UI labels.

Typography is scaled for **information density**. The default body size is 14px, while 12px and 13px are used frequently for metadata, table cells, and sidebars. High-level headlines use a subtle negative letter-spacing to maintain a premium, tight appearance.

## Layout & Spacing

The layout follows a **Fixed Grid** model for dashboard views, ensuring that data visualizations remain proportional. A 12-column system is used for the main content area, with a fixed 240px or 280px sidebar on the left.

**Density Philosophy**: 
- Use `12px` (space-md) as the standard padding for cards and containers.
- Use `8px` (space-sm) for internal element grouping.
- Vertical rhythm is tight; margins between sections should not exceed `24px` to keep related data within the same viewport.
- Mobile views transition to a fluid single-column layout with `16px` horizontal margins.

## Elevation & Depth

This design system relies on **Low-contrast outlines** rather than shadows to define depth. The goal is to keep the interface feeling "flat" and close to the glass, mimicking a professional instrument.

- **Level 0 (Background)**: #F8FAFC.
- **Level 1 (Panels/Cards)**: #FFFFFF with a 1px #E5E7EB border. No shadow.
- **Level 2 (Dropdowns/Popovers)**: #FFFFFF with a 1px #E5E7EB border and a very soft, high-diffusion shadow (`0 4px 12px rgba(0,0,0,0.05)`).
- **Level 3 (Modals)**: #FFFFFF with a slightly darker border and a 10% opacity backdrop blur.

Interactive elements (buttons) do not lift on hover; instead, they shift color or intensify their border to maintain the "precision" feel.

## Shapes

The shape language is disciplined and consistent. A **8px (0.5rem)** radius is the standard for cards, buttons, and input fields. This provides a modern feel without appearing overly "bubbly" or consumer-grade.

- **Standard (rounded-md)**: 8px. Used for almost all UI components.
- **Large (rounded-lg)**: 12px. Used exclusively for large container panels or modals.
- **Small (rounded-sm)**: 4px. Used for small tags, checkboxes, or tooltips.

## Components

### Buttons
- **Primary**: Background #2563EB, text #FFFFFF. No gradient. 8px radius. 
- **Secondary**: Background #FFFFFF, border 1px #E5E7EB, text #111827.
- **Ghost**: No background/border, text #64748B. Use for less frequent actions.
- **Sizing**: Compact (32px height) for toolbars; Standard (40px height) for forms.

### Input Fields
- 1px #E5E7EB border with 8px radius. 
- Focus state: Border #2563EB with a 2px soft outer glow in #EFF6FF.
- Use 13px (body-sm) for input text to allow for more fields in high-density forms.

### Data Tables
- Row height: 40px (Compact) to 48px (Standard).
- Header: Light gray background (#F8FAFC), 11px uppercase bold text (#64748B).
- Border-bottom: 1px #E5E7EB between rows; no vertical grid lines.

### Cards & Panels
- White background, 1px #E5E7EB border, 8px radius.
- Padding should be 16px for desktop, 12px for high-density side-panels.

### Status Chips
- Small (24px height), 4px radius.
- Use low-saturation background colors with high-saturation text for a professional look (e.g., Success: #DCFCE7 bg / #166534 text).

### AI Interaction
- Use a subtle gradient border or a Primary Blue tint for "AI-generated" content blocks to distinguish them from manual data entries.