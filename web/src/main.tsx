import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import App from './App'
import { AuthProvider, useAuth } from './lib/auth'
import Login from './pages/Login'
import Register from './pages/Register'
import CheckEmail from './pages/CheckEmail'
import Verify from './pages/Verify'
import './index.css'

function LeagueApp() {
  const { user, loading } = useAuth()
  if (loading) return <div className="auth-loading">Loading FantasyTools…</div>
  if (!user && !import.meta.env.DEV) return <Navigate to="/login" replace />
  return <App />
}

function AppRoutes() {
  return <Routes>
    <Route path="/login" element={<Login />} />
    <Route path="/register" element={<Register />} />
    <Route path="/check-email" element={<CheckEmail />} />
    <Route path="/verify" element={<Verify />} />
    <Route path="/" element={<LeagueApp />} />
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes>
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
)
