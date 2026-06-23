// src/App.tsx
import { BrowserRouter as Router, Routes, Route, Navigate } from 'react-router-dom';
import React from 'react';
import Layout from './components/Layout';
import Login from './pages/Login';
import StudentDashboard from './pages/StudentDashboard';
import ReviewCalendar from './pages/ReviewCalendar';
import AdminDashboard from './pages/AdminDashboard';
import LecturerDashboard from './pages/LecturerDashboard';
import SubmissionsPage from './pages/SubmissionsPage';

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
            return <Navigate to="/calendar" replace />;
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
                    <Route
                        path="dashboard/student"
                        element={
                            <ProtectedRoute allowedRoles={['Student']}>
                                <StudentDashboard />
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