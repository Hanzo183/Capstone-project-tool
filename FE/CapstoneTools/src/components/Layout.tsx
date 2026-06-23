// src/components/Layout.tsx
import { Outlet, NavLink, useNavigate } from 'react-router-dom';
import './Layout.css';

export default function Layout() {
    const navigate = useNavigate();
    const role = localStorage.getItem('role');

    const handleLogout = () => {
        // Evict credentials from temporary client persistence layer
        localStorage.removeItem('token');
        localStorage.removeItem('role');
        // Bounce user safely back out to authentication portal
        navigate('/login', { replace: true });
    };

    // Dynamically resolve target route nodes based on context roles
    const getDashboardPath = () => {
        if (role === 'Admin') return '/dashboard/admin';
        if (role === 'Lecturer') return '/dashboard/lecturer';
        return '/dashboard/student';
    };

    return (
        <div className="workspace-layout-container">
            {/* Master Left Side Navigation Dashboard Panel */}
            <aside className="workspace-sidebar">
                <div className="sidebar-brand-block">
                    <h1 className="brand-title">Capstone Review</h1>
                    <span className="user-role-badge">{role || 'Authorized User'}</span>
                </div>

                <nav className="sidebar-nav-links">
                    {/* General Dashboard Link */}
                    {role !== 'Lecturer' && (
                        <NavLink
                            to={getDashboardPath()}
                            className={({ isActive }) => `nav-anchor-link ${isActive ? 'active-node' : ''}`}
                        >
                            <span className="link-icon">📊</span> Dashboard
                        </NavLink>
                    )}

                    {/* Lecturer Specific Dashboard Link */}
                    {role === 'Lecturer' && (
                        <NavLink
                            to="/dashboard/lecturer"
                            className={({ isActive }) => `nav-anchor-link ${isActive ? 'active-node' : ''}`}
                        >
                            <span className="link-icon">👨‍🏫</span> Lecturer Dashboard
                        </NavLink>
                    )}

                    {/* Unified Submissions Page Link (For Students and Lecturers) */}
                    {(role === 'Student' || role === 'Lecturer') && (
                        <NavLink
                            to="/submissions"
                            className={({ isActive }) => `nav-anchor-link ${isActive ? 'active-node' : ''}`}
                        >
                            <span className="link-icon">📤</span> Submissions
                        </NavLink>
                    )}

                    {/* Global Review Calendar Link */}
                    <NavLink
                        to="/calendar"
                        className={({ isActive }) => `nav-anchor-link ${isActive ? 'active-node' : ''}`}
                    >
                        <span className="link-icon">📅</span> Review Calendar
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
                </header>
                <div className="workspace-child-view-outlet">
                    <Outlet />
                </div>
            </main>
        </div>
    );
}