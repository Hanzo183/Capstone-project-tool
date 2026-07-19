// src/components/Layout.tsx
import { useState, useEffect } from 'react';
import { Outlet, NavLink, useNavigate } from 'react-router-dom';
import './Layout.css';

export default function Layout() {
    const navigate = useNavigate();
    const role = localStorage.getItem('role');
    const fullName = localStorage.getItem('fullName') || 'User';

    const [unreadCount, setUnreadCount] = useState<number>(() => {
        const val = localStorage.getItem('unreadNotificationsCount');
        return val ? parseInt(val) : 0;
    });

    useEffect(() => {
        const handleNotifUpdate = () => {
            const val = localStorage.getItem('unreadNotificationsCount');
            setUnreadCount(val ? parseInt(val) : 0);
        };
        window.addEventListener('notificationsUpdated', handleNotifUpdate);
        return () => window.removeEventListener('notificationsUpdated', handleNotifUpdate);
    }, []);

    const handleLogout = () => {
        localStorage.removeItem('token');
        localStorage.removeItem('role');
        localStorage.removeItem('userId');
        localStorage.removeItem('fullName');
        localStorage.removeItem('email');
        navigate('/login', { replace: true });
    };

    // Dynamically resolve target route nodes based on context roles
    const getDashboardPath = () => {
        if (role === 'Admin') return '/dashboard/admin';
        if (role === 'Lecturer') return '/dashboard/lecturer';
        if (role === 'CouncilMember') return '/dashboard/council';
        return '/dashboard/student';
    };

    return (
        <div className="workspace-layout-container">
            {/* Master Left Side Navigation Dashboard Panel */}
            <aside className="workspace-sidebar">
                <div className="sidebar-brand-block">
                    <h1 className="brand-title">📘 Capstone Review</h1>
                    <span className="user-role-badge">{role || 'Authorized User'}</span>
                    <span className="user-name-badge">{fullName}</span>
                </div>

                <nav className="sidebar-nav-links">
                    {/* Dashboard - Shows different based on role */}
                    <NavLink
                        to={getDashboardPath()}
                        className={({ isActive }) => `nav-anchor-link ${isActive ? 'active-node' : ''}`}
                    >
                        <span className="link-icon">📊</span> Dashboard
                    </NavLink>

                    {/* Submissions Page (Student & Lecturer) */}
                    {(role === 'Student' || role === 'Lecturer') && (
                        <NavLink
                            to="/submissions"
                            className={({ isActive }) => `nav-anchor-link ${isActive ? 'active-node' : ''}`}
                        >
                            <span className="link-icon">📤</span> {role === 'Lecturer' ? 'Review Submission' : 'Submissions'}
                        </NavLink>
                    )}

                    {/* ✅ ADDED: Evaluation Page (Student & Lecturer & Council) */}
                    {role === 'Student' && (
                        <NavLink
                            to="/evaluation"
                            className={({ isActive }) => `nav-anchor-link ${isActive ? 'active-node' : ''}`}
                        >
                            <span className="link-icon">⭐</span> Evaluations
                        </NavLink>
                    )}

                    {/* Review Calendar (All Users) */}
                    <NavLink
                        to="/calendar"
                        className={({ isActive }) => `nav-anchor-link ${isActive ? 'active-node' : ''}`}
                    >
                        <span className="link-icon">📅</span> Review Calendar
                    </NavLink>

                    {/* Reports (Lecturer) */}
                    {role === 'Lecturer' && (
                        <NavLink
                            to="/reports"
                            className={({ isActive }) => `nav-anchor-link ${isActive ? 'active-node' : ''}`}
                        >
                            <span className="link-icon">📄</span> Reports
                        </NavLink>
                    )}

                    {/* Notifications (All Users) */}
                    <NavLink
                        to="/notifications"
                        className={({ isActive }) => `nav-anchor-link ${isActive ? 'active-node' : ''}`}
                    >
                        <span className="link-icon">🔔</span> Notifications
                        {unreadCount > 0 && <span className="notification-badge">{unreadCount}</span>}
                    </NavLink>
                </nav>

                <div className="sidebar-action-footer">
                    <button onClick={handleLogout} className="sidebar-logout-trigger">
                        <span className="link-icon">🚪</span> Sign Out
                    </button>
                </div>
            </aside>

            {/* Main Execution Workspace Panel Target Node Area */}
            <main className="workspace-view-content">
                <header className="workspace-top-bar">
                    <div className="environment-breadcrumb">
                        System Infrastructure Node &raquo; <span>Active Panel</span>
                    </div>
                    <div className="top-bar-actions">
                        <span className="user-greeting">Welcome, {fullName}</span>
                    </div>
                </header>
                <div className="workspace-child-view-outlet">
                    <Outlet />
                </div>
            </main>
        </div>
    );
}
