// src/pages/AdminDashboard.tsx
import './AdminDashboard.css';

type AdminUserRow = {
    id: string;
    name: string;
    email: string;
    role: 'Lecturer' | 'CouncilMember' | 'Student';
    status: 'Active' | 'Inactive';
};

export default function AdminDashboard() {
    // KPIs directly matching system requirements
    const statistics = {
        totalProjects: 42,
        pendingReviews: 12,
        overdueSubmissions: 3,
        activeRounds: 2
    };

    const usersList: AdminUserRow[] = [
        { id: 'U001', name: 'Dr. Nguyen Van A', email: 'anv@fpt.edu.vn', role: 'Lecturer', status: 'Active' },
        { id: 'U002', name: 'Dr. Tran B', email: 'btt@fpt.edu.vn', role: 'CouncilMember', status: 'Active' },
        { id: 'U003', name: 'Nguyen Chinh Nhan', email: 'nhanncse192706@fpt.edu.vn', role: 'Student', status: 'Active' },
    ];

    return (
        <div className="admin-container">
            {/* Title */}
            <div className="admin-header">
                <div>
                    <h2 className="admin-title">System Administration Portal</h2>
                    <p className="admin-subtitle">Monitor key system bottlenecks and configure orchestration rules.</p>
                </div>
                <div className="admin-action-bar">
                    <button className="btn-action-primary">+ New Review Round</button>
                </div>
            </div>

            {/* KPI Cards Row with scale animation */}
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

            {/* Management Workspace Split Layout */}
            <div className="admin-workspace-layout">

                {/* User Management Module */}
                <div className="glass-card table-section">
                    <div className="section-header-inline">
                        <h4 className="section-block-title">User Account Registry</h4>
                        <button className="text-link-btn">Bulk Role Assignment</button>
                    </div>

                    <div className="table-responsive-container">
                        <table className="admin-table">
                            <thead>
                                <tr>
                                    <th>User Profile</th>
                                    <th>System Role</th>
                                    <th>Account Node</th>
                                    <th>Action</th>
                                </tr>
                            </thead>
                            <tbody>
                                {usersList.map((user) => (
                                    <tr key={user.id} className="interactive-row">
                                        <td>
                                            <div className="user-profile-cell">
                                                <span className="profile-avatar-placeholder">{user.name.charAt(0)}</span>
                                                <div>
                                                    <p className="profile-name-txt">{user.name}</p>
                                                    <p className="profile-sub-email">{user.email}</p>
                                                </div>
                                            </div>
                                        </td>
                                        <td>
                                            <span className={`role-badge ${user.role.toLowerCase()}`}>
                                                {user.role}
                                            </span>
                                        </td>
                                        <td>
                                            <span className={`node-badge ${user.status.toLowerCase()}`}>
                                                {user.status}
                                            </span>
                                        </td>
                                        <td>
                                            <button className="row-action-btn">Edit</button>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                </div>

                {/* System Operations Control Box */}
                <div className="glass-card controls-section">
                    <h4 className="section-block-title">System Orchestration Logs</h4>
                    <div className="operations-stack">
                        <div className="operation-item-box">
                            <div className="op-info">
                                <h5>Background Job Monitor</h5>
                                <p>Hangfire cron loops executing reports</p>
                            </div>
                            <button className="btn-op-trigger">Trigger Execution</button>
                        </div>

                        <div className="operation-item-box">
                            <div className="op-info">
                                <h5>Message Broker Exchange</h5>
                                <p>RabbitMQ exchange status checking</p>
                            </div>
                            <span className="broker-status-tag healthy">Active Node</span>
                        </div>
                    </div>
                </div>

            </div>
        </div>
    );
}