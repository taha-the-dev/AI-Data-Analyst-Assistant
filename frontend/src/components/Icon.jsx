/**
 * Material Symbols Outlined, the icon set the design specifies.
 * `fill` switches the variable font's FILL axis on, as the active nav item and
 * the insight cards do in the source design.
 */
export default function Icon({ name, size = 20, fill = false, className = '', ...rest }) {
  return (
    <span
      className={`material-symbols-outlined ${fill ? 'icon-fill' : ''} ${className}`}
      style={{ fontSize: `${size}px` }}
      aria-hidden="true"
      {...rest}
    >
      {name}
    </span>
  )
}
