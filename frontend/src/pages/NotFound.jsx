import { Link } from 'react-router-dom'
import { PageCanvas } from '../components/AppShell'
import { Panel, EmptyState } from '../components/ui'

export default function NotFound() {
  return (
    <PageCanvas>
      <Panel>
        <EmptyState
          icon="explore_off"
          title="There is nothing at this address"
          body="The page may have moved. Head back to the dashboard to pick up where you left off."
          action={
            <Link
              to="/"
              className="inline-flex items-center gap-sm rounded-lg font-label-bold text-label-bold px-md py-sm bg-primary-container text-on-primary hover:bg-primary-hover transition-colors"
            >
              Go to Dashboard
            </Link>
          }
        />
      </Panel>
    </PageCanvas>
  )
}
