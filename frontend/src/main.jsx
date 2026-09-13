import React from 'react'
import ReactDOM from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App'
import './index.css'

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    {/* On react-router 7 the behaviours this used to opt into are the
        defaults, so the flags that carried the app across the major are gone
        and there is nothing left to configure here. */}
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </React.StrictMode>
)
