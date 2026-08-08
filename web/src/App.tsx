import { Navigate, Route, Routes } from 'react-router-dom'
import { useAuth } from './lib/auth'
import CheckEmail from './pages/CheckEmail'
import Home from './pages/Home'
import Login from './pages/Login'
import Register from './pages/Register'
import Verify from './pages/Verify'

export default function App() {
  const { user, loading } = useAuth()

  if (loading) {
    return <div className="p-6 text-slate-600">Loading…</div>
  }

  return (
    <Routes>
      <Route path="/login" element={user ? <Navigate to="/" replace /> : <Login />} />
      <Route path="/register" element={user ? <Navigate to="/" replace /> : <Register />} />

      {/* Public: both are reached from an email or while signed out. */}
      <Route path="/check-email" element={<CheckEmail />} />
      <Route path="/verify" element={<Verify />} />

      <Route path="/" element={user ? <Home /> : <Navigate to="/login" replace />} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
