import { Component } from 'react'
import Icon from './Icon'

/** Keeps one render error from blanking the whole app. */
export default class ErrorBoundary extends Component {
  state = { error: null }

  static getDerivedStateFromError(error) {
    return { error }
  }

  componentDidCatch(error, info) {
    console.error('Render failed:', error, info)
  }

  render() {
    if (!this.state.error) return this.props.children

    return (
      <div className="min-h-screen flex items-center justify-center p-lg bg-background">
        <div
          role="alert"
          className="max-w-md w-full bg-surface-container-lowest border border-error-container rounded-card ambient-shadow p-lg flex flex-col gap-sm"
        >
          <span className="w-9 h-9 rounded-full bg-error-container text-error flex items-center justify-center">
            <Icon name="error" size={18} />
          </span>
          <h1 className="font-section-title text-section-title text-on-surface mt-xs">
            This screen stopped rendering
          </h1>
          <p className="font-body-main text-body-main text-on-surface-variant">
            {this.state.error?.message || 'An unexpected error occurred.'} Reload the page to carry on.
            The rest of your data is unaffected.
          </p>
          <div className="flex gap-sm mt-sm">
            <button
              onClick={() => window.location.reload()}
              className="inline-flex items-center gap-sm rounded-lg font-label-bold text-label-bold px-md py-sm bg-primary-container text-on-primary hover:bg-primary-hover transition-colors"
            >
              <Icon name="refresh" size={18} />
              Reload page
            </button>
          </div>
        </div>
      </div>
    )
  }
}
