// src/App.tsx
import { BrowserRouter as Router, Routes, Route, Navigate } from 'react-router-dom';
import React from 'react';
import Layout from './components/Layout';
import Login from './pages/Login';
import Register from './pages/Register';
import StudentDashboard from './pages/StudentDashboard';
import ReviewCalendar from './pages/ReviewCalendar';
import AdminDashboard from './pages/AdminDashboard';
import LecturerDashboard from './pages/LecturerDashboard';
import SubmissionsPage from './pages/SubmissionsPage';
import EvaluationPage from './pages/EvaluationPage';
import CouncilDashboard from './pages/CouncilDashboard';
import NotificationsPage from './pages/NotificationsPage';
import ReportsPage from './pages/ReportsPage';

// Client-side session and role status helper
const getLocalUser = () => {
    const token = localStorage.getItem('token');
    const role = localStorage.getItem('role') as 'Student' | 'Admin' | 'Lecturer' | 'CouncilMember' | null;
    return { isAuthenticated: !!token, role };
};

// Index route coordinator targeting correct role workspace landing zones
function RootRedirect() {
    const user = getLocalUser();

    if (!user.isAuthenticated) {
        return <Navigate to="/login" replace />;
    }

    switch (user.role) {
        case 'Admin':
            return <Navigate to="/dashboard/admin" replace />;
        case 'Student':
            return <Navigate to="/dashboard/student" replace />;
        case 'Lecturer':
            return <Navigate to="/dashboard/lecturer" replace />;
        case 'CouncilMember':
            return <Navigate to="/dashboard/council" replace />;
        default:
            return <Navigate to="/login" replace />;
    }
}

// Strict typed guard configuration wrapper block
interface ProtectedRouteProps {
    children: React.ReactNode;
    allowedRoles: ('Student' | 'Admin' | 'Lecturer' | 'CouncilMember')[];
}

function ProtectedRoute({ children, allowedRoles }: ProtectedRouteProps) {
    const user = getLocalUser();

    if (!user.isAuthenticated) {
        return <Navigate to="/login" replace />;
    }

    if (!allowedRoles.includes(user.role!)) {
        // If authenticated but visiting unassigned segment nodes, return safely to home evaluation root
        return <Navigate to="/" replace />;
    }

    return <>{children}</>;
}

function App() {
    return (
        <Router>
            <Routes>
                {/* Public Route */}
                <Route path="/login" element={<Login />} />
                <Route path="/register" element={<Register />} />

                {/* Protected Routes Wrapper */}
                <Route path="/" element={<Layout />}>
                    {/* Dynamic evaluation branch based on active state criteria */}
                    <Route index element={<RootRedirect />} />
                    {/* Standalone Artifact Evaluation Repositories */}
                    <Route
                        path="submissions"
                        element={
                            <ProtectedRoute allowedRoles={['Student', 'Lecturer']}>
                                <SubmissionsPage />
                            </ProtectedRoute>
                        }
                    />
                    <Route path="evaluation"
                        element={
                            <ProtectedRoute allowedRoles={['Student', 'Admin', 'Lecturer', 'CouncilMember']}>
                                <EvaluationPage />
                            </ProtectedRoute>
                        }
                    />
                    <Route
                        path="dashboard/student"
                        element={
                            <ProtectedRoute allowedRoles={['Student']}>
                                <StudentDashboard />
                            </ProtectedRoute>
                        }
                    />
                    <Route
                        path="reports"
                        element={
                            <ProtectedRoute allowedRoles={['Lecturer']}>
                                <ReportsPage />
                            </ProtectedRoute>
                        }
                    />

                    <Route
                        path="dashboard/admin"
                        element={
                            <ProtectedRoute allowedRoles={['Admin']}>
                                <AdminDashboard />
                            </ProtectedRoute>
                        }
                    />

                    <Route
                        path="calendar"
                        element={
                            <ProtectedRoute allowedRoles={['Admin', 'Lecturer', 'CouncilMember', 'Student']}>
                                <ReviewCalendar />
                            </ProtectedRoute>
                        }
                    />

                    <Route
                        path="dashboard/council"
                        element={
                            <ProtectedRoute allowedRoles={['CouncilMember']}>
                                <CouncilDashboard />
                            </ProtectedRoute>
                        }
                    />

                    <Route
                        path="notifications"
                        element={
                            <ProtectedRoute allowedRoles={['Student', 'Admin', 'Lecturer', 'CouncilMember']}>
                                <NotificationsPage />
                            </ProtectedRoute>
                        }
                    />

                    <Route
                        path="dashboard/lecturer"
                        element={
                            <ProtectedRoute allowedRoles={['Lecturer']}>
                                <LecturerDashboard />
                            </ProtectedRoute>
                        }
                    />
                </Route>

                {/* Global Wildcard Fallback Redirect */}
                <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
        </Router>
    );
}

export default App;