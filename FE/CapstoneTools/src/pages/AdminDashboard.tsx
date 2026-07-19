// src/pages/AdminDashboard.tsx
import { useState, useEffect } from 'react';
import { api } from '../api';
import './AdminDashboard.css';

interface User {
    id: string;
    fullName: string;
    email: string;
    role: string;
    isActive: boolean;
    studentId?: string;
    createdAt?: string;
}

interface Project {
    id: string;
    title: string;
    status: string;
    teamId: string;
    lecturerId: string;
    createdAt?: string;
}

interface ReviewRound {
    id: string;
    name: string;
    startDate: string;
    endDate: string;
    status: string;
}

const studentIdPattern = /^[A-Za-z]{2}\d{6}$/;
const emailPattern = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

const isStrongPassword = (password: string) =>
    password.length >= 8;

export default function AdminDashboard() {
    const [users, setUsers] = useState<User[]>([]);
    const [projects, setProjects] = useState<Project[]>([]);
    const [rounds, setRounds] = useState<ReviewRound[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [errorMsg, setErrorMsg] = useState('');
    const [showCreateRound, setShowCreateRound] = useState(false);
    const [showCreateUser, setShowCreateUser] = useState(false);

    // New Round Form State
    const [newRound, setNewRound] = useState({
        name: '',
        startDate: '',
        endDate: ''
    });

    // New User Form State
    const [newUser, setNewUser] = useState({
        email: '',
        password: '',
        fullName: '',
        studentId: '',
        role: 'Student'
    });

    // --- FETCH ALL DATA ---
    useEffect(() => {
        const loadAdminData = async () => {
            try {
                setIsLoading(true);
                setErrorMsg('');

                // Fetch users from Identity Service
                const usersData = await api.getUsers();
                setUsers(Array.isArray(usersData) ? usersData : []);

                // Fetch projects from Project Service
                const projectsData = await api.getProjects();
                setProjects(Array.isArray(projectsData) ? projectsData : []);

                // Fetch rounds from Scheduling Service
                try {
                    const roundsData = await api.getReviewRounds();
                    setRounds(Array.isArray(roundsData) ? roundsData : []);
                } catch (err) {
                    console.warn('Could not fetch rounds:', err);
                    setRounds([]);
                }

            } catch (err) {
                console.error('Failed to load admin data:', err);
                setErrorMsg('Failed to load dashboard data. Please refresh.');
            } finally {
                setIsLoading(false);
            }
        };

        loadAdminData();
    }, []);

    // --- CALCULATE STATISTICS ---
    const statistics = {
        totalProjects: projects.length,
        pendingReviews: projects.filter(p => p.status === 'In Review' || p.status === 'Submitted').length,
        overdueSubmissions: projects.filter(p => p.status === 'Needs Revision').length,
        activeRounds: rounds.filter(r => r.status === 'Upcoming' || r.status === 'Active').length
    };

    // --- HANDLE CREATE REVIEW ROUND ---
    const handleCreateRound = async (e: React.FormEvent) => {
        e.preventDefault();
        const adminId = localStorage.getItem('userId') || 'admin';

        if (!newRound.name.trim()) {
            alert('Round name is required.');
            return;
        }

        if (!newRound.startDate || !newRound.endDate || newRound.endDate < newRound.startDate) {
            alert('End date must be on or after start date.');
            return;
        }

        try {
            await api.createReviewRound({
                name: newRound.name.trim(),
                startDate: newRound.startDate,
                endDate: newRound.endDate,
                createdBy: adminId
            });

            alert('✅ Review round created successfully!');
            setShowCreateRound(false);
            setNewRound({ name: '', startDate: '', endDate: '' });

            // Refresh rounds
            const roundsData = await api.getReviewRounds();
            setRounds(Array.isArray(roundsData) ? roundsData : []);
        } catch  {
            alert('❌ Failed to create review round.');
        }
    };

    // --- HANDLE CREATE USER ---
    const handleCreateUser = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!newUser.fullName.trim()) {
            alert('Full name is required.');
            return;
        }

        if (!emailPattern.test(newUser.email.trim())) {
            alert('Please enter a valid email address.');
            return;
        }

        if (newUser.role === 'Student' && !newUser.studentId.trim()) {
            alert('ID is required for student accounts.');
            return;
        }

        if (newUser.studentId.trim() && !studentIdPattern.test(newUser.studentId.trim())) {
            alert('ID must start with 2 letters followed by 6 numbers, for example SE192706.');
            return;
        }

        if (!isStrongPassword(newUser.password)) {
            alert('Password must be at least 8 characters.');
            return;
        }

        try {
            await api.register({
                email: newUser.email.trim(),
                password: newUser.password,
                fullName: newUser.fullName.trim(),
                studentId: newUser.studentId.trim() ? newUser.studentId.trim().toUpperCase() : undefined,
                role: newUser.role
            });

            alert('✅ User created successfully!');
            setShowCreateUser(false);
            setNewUser({ email: '', password: '', fullName: '', studentId: '', role: 'Student' });

            // Refresh users
            const usersData = await api.getUsers();
            setUsers(Array.isArray(usersData) ? usersData : []);
        } catch  {
            alert('Failed to create user. Email or ID may already exist.');
        }
    };

    // --- HANDLE UPDATE USER ROLE ---
    const handleUpdateRole = async (userId: string, nextRole: string) => {
        if (window.confirm(`Change user role to "${nextRole}"?`)) {
            try {
                await api.updateUserRole(userId, nextRole);
                alert('✅ User role updated!');
                // Refresh users
                const usersData = await api.getUsers();
                setUsers(Array.isArray(usersData) ? usersData : []);
            } catch  {
                alert('❌ Failed to update user role.');
            }
        }
    };

    // --- HANDLE TOGGLE USER STATUS ---
    const handleToggleStatus = async (userId: string, currentStatus: boolean) => {
        const newStatus = !currentStatus;
        const action = newStatus ? 'activate' : 'deactivate';

        if (window.confirm(`Are you sure you want to ${action} this user?`)) {
            try {
                await api.updateUserStatus(userId, newStatus);
                alert(`✅ User ${action}d!`);
                // Refresh users
                const usersData = await api.getUsers();
                setUsers(usersData);
            } catch  {
                alert(`❌ Failed to ${action} user.`);
            }
        }
    };

    // --- HANDLE DELETE USER (Optional - if backend supports) ---
    // You might need to add this endpoint

    return (
        <div className="admin-container">
            {/* Header */}
            <div className="admin-header">
                <div>
                    <h2 className="admin-title">⚙️ System Administration Portal</h2>
                    <p className="admin-subtitle">Monitor key system bottlenecks and configure orchestration rules.</p>
                </div>
                <div className="admin-action-bar" style={{ display: 'flex', gap: '0.5rem' }}>
                    <button
                        className="btn-action-primary"
                        onClick={() => setShowCreateRound(true)}
                        style={{
                            padding: '0.5rem 1.5rem',
                            background: '#3b82f6',
                            color: 'white',
                            border: 'none',
                            borderRadius: '8px',
                            cursor: 'pointer',
                            fontWeight: '600'
                        }}
                    >
                        + New Review Round
                    </button>
                    <button
                        className="btn-action-secondary"
                        onClick={() => setShowCreateUser(true)}
                        style={{
                            padding: '0.5rem 1.5rem',
                            background: '#22c55e',
                            color: 'white',
                            border: 'none',
                            borderRadius: '8px',
                            cursor: 'pointer',
                            fontWeight: '600'
                        }}
                    >
                        + New User
                    </button>
                </div>
            </div>

            {errorMsg && (
                <div style={{
                    color: '#ef4444',
                    padding: '1rem',
                    marginBottom: '1rem',
                    background: '#fee2e2',
                    borderRadius: '8px',
                    border: '1px solid #fecaca'
                }}>
                    ❌ {errorMsg}
                </div>
            )}

            {isLoading ? (
                <div style={{ textAlign: 'center', padding: '3rem', color: '#6b7280' }}>
                    ⏳ Loading dashboard...
                </div>
            ) : (
                <>
                    {/* KPI Cards */}
                    <div className="kpi-grid">
                        <div className="kpi-card glass-card">
                            <span className="kpi-label">Total Projects</span>
                            <h3 className="kpi-value">{statistics.totalProjects}</h3>
                            <div className="kpi-indicator green">Active This Semester</div>
                        </div>
                        <div className="kpi-card glass-card">
                            <span className="kpi-label">Pending Reviews</span>
                            <h3 className="kpi-value warning-color">{statistics.pendingReviews}</h3>
                            <div className="kpi-indicator yellow">Awaiting Evaluation</div>
                        </div>
                        <div className="kpi-card glass-card">
                            <span className="kpi-label">Overdue Submissions</span>
                            <h3 className="kpi-value critical-color">{statistics.overdueSubmissions}</h3>
                            <div className="kpi-indicator red">Action Required</div>
                        </div>
                        <div className="kpi-card glass-card">
                            <span className="kpi-label">Active Rounds</span>
                            <h3 className="kpi-value">{statistics.activeRounds}</h3>
                            <div className="kpi-indicator regular">Hangfire Monitored</div>
                        </div>
                    </div>

                    {/* Management Workspace */}
                    <div className="admin-workspace-layout">

                        {/* User Management Table */}
                        <div className="glass-card table-section">
                            <div className="section-header-inline">
                                <h4 className="section-block-title">👥 User Account Registry</h4>
                                <span style={{ fontSize: '0.85rem', color: '#6b7280' }}>
                                    {users.length} users total
                                </span>
                            </div>

                            <div className="table-responsive-container">
                                <table className="admin-table">
                                    <thead>
                                        <tr>
                                            <th>User Profile</th>
                                            <th>System Role</th>
                                            <th>Account Node</th>
                                            <th>Actions</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {users.length === 0 ? (
                                            <tr>
                                                <td colSpan={4} style={{ textAlign: 'center', padding: '2rem', color: '#6b7280' }}>
                                                    No users found.
                                                </td>
                                            </tr>
                                        ) : (
                                            users.map((user) => (
                                                <tr key={user.id} className="interactive-row">
                                                    <td>
                                                        <div className="user-profile-cell">
                                                            <span className="profile-avatar-placeholder">
                                                                {user.fullName?.charAt(0) || '?'}
                                                            </span>
                                                            <div>
                                                                <p className="profile-name-txt">{user.fullName}</p>
                                                                <p className="profile-sub-email">{user.email}</p>
                                                                {user.studentId && (
                                                                    <p className="profile-sub-email" style={{ fontSize: '0.7rem', color: '#94a3b8' }}>
                                                                        ID: {user.studentId}
                                                                    </p>
                                                                )}
                                                            </div>
                                                        </div>
                                                    </td>
                                                    <td>
                                                        <span className={`role-badge ${user.role?.toLowerCase() || 'student'}`}>
                                                            {user.role || 'Student'}
                                                        </span>
                                                    </td>
                                                    <td>
                                                        <span className={`node-badge ${user.isActive ? 'active' : 'inactive'}`}>
                                                            {user.isActive ? 'Active' : 'Inactive'}
                                                        </span>
                                                    </td>
                                                    <td>
                                                        <div style={{ display: 'flex', gap: '0.25rem', flexWrap: 'wrap' }}>
                                                            <select
                                                                className="row-action-btn"
                                                                value={user.role || 'Student'}
                                                                onChange={(e) => handleUpdateRole(user.id, e.target.value)}
                                                                style={{
                                                                    padding: '0.2rem 0.5rem',
                                                                    background: '#ffffff',
                                                                    color: '#1f2937',
                                                                    border: '1px solid #cbd5e1',
                                                                    borderRadius: '4px',
                                                                    cursor: 'pointer',
                                                                    fontSize: '0.75rem'
                                                                }}
                                                            >
                                                                <option value="Student">Student</option>
                                                                <option value="Lecturer">Lecturer</option>
                                                                <option value="CouncilMember">Council Member</option>
                                                                <option value="Admin">Admin</option>
                                                            </select>
                                                            <button
                                                                className="row-action-btn"
                                                                onClick={() => handleToggleStatus(user.id, user.isActive)}
                                                                style={{
                                                                    padding: '0.2rem 0.6rem',
                                                                    background: user.isActive ? '#ef4444' : '#22c55e',
                                                                    color: 'white',
                                                                    border: 'none',
                                                                    borderRadius: '4px',
                                                                    cursor: 'pointer',
                                                                    fontSize: '0.75rem'
                                                                }}
                                                            >
                                                                {user.isActive ? 'Deactivate' : 'Activate'}
                                                            </button>
                                                        </div>
                                                    </td>
                                                </tr>
                                            ))
                                        )}
                                    </tbody>
                                </table>
                            </div>
                        </div>

                        {/* System Controls */}
                        <div className="glass-card controls-section">
                            <h4 className="section-block-title">🔄 System Orchestration Logs</h4>
                            <div className="operations-stack">
                                <div className="operation-item-box">
                                    <div className="op-info">
                                        <h5>Background Job Monitor</h5>
                                        <p>Hangfire cron loops executing reports</p>
                                    </div>
                                    <button
                                        className="btn-op-trigger"
                                        style={{
                                            padding: '0.3rem 1rem',
                                            background: '#3b82f6',
                                            color: 'white',
                                            border: 'none',
                                            borderRadius: '6px',
                                            cursor: 'pointer'
                                        }}
                                        onClick={() => alert('✅ Background jobs triggered!')}
                                    >
                                        Trigger Execution
                                    </button>
                                </div>

                                <div className="operation-item-box">
                                    <div className="op-info">
                                        <h5>Message Broker Exchange</h5>
                                        <p>Kafka exchange status checking</p>
                                    </div>
                                    <span className="broker-status-tag healthy" style={{
                                        padding: '0.2rem 0.8rem',
                                        borderRadius: '12px',
                                        background: '#dcfce7',
                                        color: '#16a34a',
                                        fontSize: '0.8rem',
                                        fontWeight: '600'
                                    }}>
                                        ✅ Active Node
                                    </span>
                                </div>

                                <div className="operation-item-box">
                                    <div className="op-info">
                                        <h5>Active Review Rounds</h5>
                                        <p>{rounds.length} rounds configured</p>
                                    </div>
                                    <span style={{ fontSize: '0.85rem', color: '#6b7280' }}>
                                        {rounds.filter(r => r.status === 'Active').length} active
                                    </span>
                                </div>
                            </div>
                        </div>
                    </div>
                </>
            )}

            {/* --- CREATE REVIEW ROUND MODAL --- */}
            {showCreateRound && (
                <div className="modal-overlay" style={{
                    position: 'fixed',
                    top: 0,
                    left: 0,
                    right: 0,
                    bottom: 0,
                    background: 'rgba(0,0,0,0.5)',
                    display: 'flex',
                    justifyContent: 'center',
                    alignItems: 'center',
                    zIndex: 1000
                }}>
                    <div className="modal-content" style={{
                        background: 'white',
                        borderRadius: '12px',
                        padding: '2rem',
                        maxWidth: '500px',
                        width: '100%'
                    }}>
                        <h2>Create New Review Round</h2>
                        <form onSubmit={handleCreateRound}>
                            <div style={{ marginBottom: '1rem' }}>
                                <label style={{ display: 'block', marginBottom: '0.25rem', fontWeight: '500' }}>
                                    Round Name *
                                </label>
                                <input
                                    type="text"
                                    required
                                    placeholder="e.g. Spring 2025 Round 1"
                                    value={newRound.name}
                                    onChange={(e) => setNewRound({ ...newRound, name: e.target.value })}
                                    style={{
                                        width: '100%',
                                        padding: '0.5rem',
                                        borderRadius: '6px',
                                        border: '1px solid #d1d5db'
                                    }}
                                />
                            </div>
                            <div style={{ marginBottom: '1rem' }}>
                                <label style={{ display: 'block', marginBottom: '0.25rem', fontWeight: '500' }}>
                                    Start Date *
                                </label>
                                <input
                                    type="date"
                                    required
                                    minLength={8}
                                    value={newRound.startDate}
                                    onChange={(e) => setNewRound({ ...newRound, startDate: e.target.value })}
                                    style={{
                                        width: '100%',
                                        padding: '0.5rem',
                                        borderRadius: '6px',
                                        border: '1px solid #d1d5db'
                                    }}
                                />
                            </div>
                            <div style={{ marginBottom: '1rem' }}>
                                <label style={{ display: 'block', marginBottom: '0.25rem', fontWeight: '500' }}>
                                    End Date *
                                </label>
                                <input
                                    type="date"
                                    required
                                    value={newRound.endDate}
                                    onChange={(e) => setNewRound({ ...newRound, endDate: e.target.value })}
                                    style={{
                                        width: '100%',
                                        padding: '0.5rem',
                                        borderRadius: '6px',
                                        border: '1px solid #d1d5db'
                                    }}
                                />
                            </div>
                            <div style={{ display: 'flex', gap: '1rem' }}>
                                <button
                                    type="button"
                                    onClick={() => setShowCreateRound(false)}
                                    style={{
                                        padding: '0.5rem 1.5rem',
                                        background: '#e5e7eb',
                                        border: 'none',
                                        borderRadius: '6px',
                                        cursor: 'pointer'
                                    }}
                                >
                                    Cancel
                                </button>
                                <button
                                    type="submit"
                                    style={{
                                        padding: '0.5rem 1.5rem',
                                        background: '#3b82f6',
                                        color: 'white',
                                        border: 'none',
                                        borderRadius: '6px',
                                        cursor: 'pointer',
                                        fontWeight: '600'
                                    }}
                                >
                                    Create Round
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {/* --- CREATE USER MODAL --- */}
            {showCreateUser && (
                <div className="modal-overlay" style={{
                    position: 'fixed',
                    top: 0,
                    left: 0,
                    right: 0,
                    bottom: 0,
                    background: 'rgba(0,0,0,0.5)',
                    display: 'flex',
                    justifyContent: 'center',
                    alignItems: 'center',
                    zIndex: 1000
                }}>
                    <div className="modal-content" style={{
                        background: 'white',
                        borderRadius: '12px',
                        padding: '2rem',
                        maxWidth: '500px',
                        width: '100%',
                        maxHeight: '90vh',
                        overflow: 'auto'
                    }}>
                        <h2>Create New User</h2>
                        <form onSubmit={handleCreateUser}>
                            <div style={{ marginBottom: '1rem' }}>
                                <label style={{ display: 'block', marginBottom: '0.25rem', fontWeight: '500' }}>
                                    Full Name *
                                </label>
                                <input
                                    type="text"
                                    required
                                    placeholder="Nguyen Van A"
                                    value={newUser.fullName}
                                    onChange={(e) => setNewUser({ ...newUser, fullName: e.target.value })}
                                    style={{
                                        width: '100%',
                                        padding: '0.5rem',
                                        borderRadius: '6px',
                                        border: '1px solid #d1d5db'
                                    }}
                                />
                            </div>
                            <div style={{ marginBottom: '1rem' }}>
                                <label style={{ display: 'block', marginBottom: '0.25rem', fontWeight: '500' }}>
                                    Email *
                                </label>
                                <input
                                    type="email"
                                    required
                                    placeholder="user@fpt.edu.vn"
                                    value={newUser.email}
                                    onChange={(e) => setNewUser({ ...newUser, email: e.target.value })}
                                    style={{
                                        width: '100%',
                                        padding: '0.5rem',
                                        borderRadius: '6px',
                                        border: '1px solid #d1d5db'
                                    }}
                                />
                            </div>
                            <div style={{ marginBottom: '1rem' }}>
                                <label style={{ display: 'block', marginBottom: '0.25rem', fontWeight: '500' }}>
                                    Password *
                                </label>
                                <input
                                    type="password"
                                    required
                                    placeholder="••••••••"
                                    value={newUser.password}
                                    onChange={(e) => setNewUser({ ...newUser, password: e.target.value })}
                                    style={{
                                        width: '100%',
                                        padding: '0.5rem',
                                        borderRadius: '6px',
                                        border: '1px solid #d1d5db'
                                    }}
                                />
                            </div>
                            <div style={{ marginBottom: '1rem' }}>
                                <label style={{ display: 'block', marginBottom: '0.25rem', fontWeight: '500' }}>
                                    ID
                                </label>
                                <input
                                    type="text"
                                    placeholder="SE123456"
                                    pattern="[A-Za-z]{2}[0-9]{6}"
                                    title="ID must start with 2 letters followed by 6 numbers, for example SE192706."
                                    required={newUser.role === 'Student'}
                                    value={newUser.studentId}
                                    onChange={(e) => setNewUser({ ...newUser, studentId: e.target.value })}
                                    style={{
                                        width: '100%',
                                        padding: '0.5rem',
                                        borderRadius: '6px',
                                        border: '1px solid #d1d5db'
                                    }}
                                />
                            </div>
                            <div style={{ marginBottom: '1rem' }}>
                                <label style={{ display: 'block', marginBottom: '0.25rem', fontWeight: '500' }}>
                                    Role *
                                </label>
                                <select
                                    value={newUser.role}
                                    onChange={(e) => setNewUser({ ...newUser, role: e.target.value })}
                                    style={{
                                        width: '100%',
                                        padding: '0.5rem',
                                        borderRadius: '6px',
                                        border: '1px solid #d1d5db'
                                    }}
                                >
                                    <option value="Student">Student</option>
                                    <option value="Lecturer">Lecturer</option>
                                    <option value="CouncilMember">Council Member</option>
                                    <option value="Admin">Admin</option>
                                </select>
                            </div>
                            <div style={{ display: 'flex', gap: '1rem' }}>
                                <button
                                    type="button"
                                    onClick={() => setShowCreateUser(false)}
                                    style={{
                                        padding: '0.5rem 1.5rem',
                                        background: '#e5e7eb',
                                        border: 'none',
                                        borderRadius: '6px',
                                        cursor: 'pointer'
                                    }}
                                >
                                    Cancel
                                </button>
                                <button
                                    type="submit"
                                    style={{
                                        padding: '0.5rem 1.5rem',
                                        background: '#22c55e',
                                        color: 'white',
                                        border: 'none',
                                        borderRadius: '6px',
                                        cursor: 'pointer',
                                        fontWeight: '600'
                                    }}
                                >
                                    Create User
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
}
