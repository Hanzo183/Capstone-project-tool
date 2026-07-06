// src/pages/LecturerDashboard.tsx
import { useState, useEffect } from 'react';
import { api } from '../api';
import './LecturerDashboard.css';

interface MentoredGroup {
    id: string;
    topicName: string;
    studentLeader: string;
    memberCount: number;
    submissionStatus: 'Submitted' | 'Pending' | 'Overdue';
    lastUpdated: string;
    projectId: string;
    teamId?: string;
    status?: string;
}

interface UpcomingCouncil {
    id: string;
    timeSlot: string;
    room: string;
    projectTitle: string;
    role: 'Chairman' | 'Secretary' | 'Member';
    projectId: string;
}

interface BackendProject {
    id: string;
    title: string;
    teamId: string;
    teamLeaderId?: string;
    lecturerId?: string;
    status: string;
    roundId?: string;
    createdAt?: string;
    updatedAt?: string;
    description?: string;
}

export default function LecturerDashboard() {
    const [mentoredGroups, setMentoredGroups] = useState<MentoredGroup[]>([]);
    const [upcomingCouncils, setUpcomingCouncils] = useState<UpcomingCouncil[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [errorMsg, setErrorMsg] = useState('');
    const [showCreateModal, setShowCreateModal] = useState(false);

    // Get current lecturer info from localStorage
    const lecturerId = localStorage.getItem('userId') || 'SE192879';
    const lecturerName = localStorage.getItem('fullName') || 'Tran Tuan Minh';

    useEffect(() => {
        const loadDashboardData = async () => {
            try {
                setIsLoading(true);
                setErrorMsg('');

                // --- 1. FETCH ALL PROJECTS ---
                const projectsData = await api.getProjects();

                // Filter projects where this lecturer is the supervisor
                const lecturerProjects = projectsData.filter(
                    (p: BackendProject) => p.lecturerId === lecturerId || p.lecturerId === null
                );

                // Map to MentoredGroup format
                const mappedGroups: MentoredGroup[] = lecturerProjects.map((project: BackendProject) => {
                    let submissionStatus: 'Submitted' | 'Pending' | 'Overdue' = 'Pending';
                    if (project.status === 'Submitted' || project.status === 'In Review') {
                        submissionStatus = 'Submitted';
                    } else if (project.status === 'Needs Revision') {
                        submissionStatus = 'Overdue';
                    }

                    return {
                        id: project.id,
                        projectId: project.id,
                        topicName: project.title || 'Untitled Project',
                        studentLeader: project.teamLeaderId || 'Not Assigned',
                        memberCount: 4,
                        submissionStatus: submissionStatus,
                        lastUpdated: project.updatedAt
                            ? new Date(project.updatedAt).toLocaleString()
                            : 'Recent',
                        teamId: project.teamId,
                        status: project.status
                    };
                });

                setMentoredGroups(mappedGroups);

                // --- 2. FETCH SCHEDULING SLOTS (Mock for now) ---
                const mockCouncils: UpcomingCouncil[] = [
                    {
                        id: 'C01',
                        timeSlot: 'June 25, 10:00 AM',
                        room: 'Alpha 105',
                        projectTitle: 'IoT Home Automation Hub',
                        role: 'Chairman',
                        projectId: 'PRJ-1001'
                    },
                    {
                        id: 'C02',
                        timeSlot: 'June 25, 02:00 PM',
                        room: 'Beta 202',
                        projectTitle: 'Mobile Health Diagnostics',
                        role: 'Member',
                        projectId: 'PRJ-1002'
                    },
                ];

                setUpcomingCouncils(mockCouncils);

            } catch (err) {
                console.error('Failed to load dashboard:', err);
                setErrorMsg('Failed to load dashboard data. Please refresh.');
            } finally {
                setIsLoading(false);
            }
        };

        loadDashboardData();
    }, [lecturerId]);

    // --- HANDLE REVIEW ARTIFACTS ---
    const handleReviewArtifacts = (projectId: string) => {
        // ✅ FIXED: Use window.open or navigate without full reload
        // Option 1: Use window.location (causes full reload but works)
        // window.location.href = `/submissions?projectId=${projectId}`;

        // Option 2: Better - use window.open for new tab
        window.open(`/submissions?projectId=${projectId}`, '_blank');

        // Option 3: If using React Router (recommended):
        // navigate(`/submissions?projectId=${projectId}`);
    };

    // --- HANDLE OPEN EVALUATION ---
    const handleOpenEvaluation = (projectId: string, projectTitle: string) => {
        // ✅ FIXED: Use alert or navigate
        alert(`Opening evaluation sheet for: ${projectTitle}`);
        // navigate(`/evaluation/${projectId}`);
    };

    // --- HANDLE CREATE PROJECT ---
    const handleCreateProject = () => {
        setShowCreateModal(true);
        // Or navigate to create page:
        // navigate('/projects/create');
    };

    // --- HANDLE CREATE PROJECT SUBMIT ---
    const handleCreateProjectSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        // Get form data
        const form = e.target as HTMLFormElement;
        const formData = new FormData(form);

        const projectData = {
            title: formData.get('title') as string,
            description: formData.get('description') as string,
            teamId: formData.get('teamId') as string,
            lecturerId: lecturerId,
            teamLeaderId: formData.get('teamLeaderId') as string,
            status: 'Draft'
        };

        try {
            const newProject = await api.createProject(projectData);
            alert(`✅ Project "${newProject.title}" created successfully!`);
            setShowCreateModal(false);
            // Refresh the dashboard
            window.location.reload();
        } catch  {
            alert('❌ Failed to create project. Please try again.');
        }
    };

    return (
        <div className="lecturer-dashboard-container">
            {/* Dashboard Top Header Section */}
            <div className="lecturer-header-block">
                <div>
                    <h2 className="lecturer-title">👨‍🏫 Lecturer Workspace Portal</h2>
                    <p className="lecturer-subtitle">
                        Welcome back, {lecturerName}! Track your mentored thesis cohorts and defense council assignments.
                    </p>
                </div>
                <div className="quick-stats-row">
                    <div className="stat-pill">
                        <span className="stat-dot green"></span>
                        <strong>{mentoredGroups.length}</strong> Mentored Groups
                    </div>
                    <div className="stat-pill">
                        <span className="stat-dot indigo"></span>
                        <strong>{upcomingCouncils.length}</strong> Active Councils
                    </div>
                    <button
                        className="btn-create-project"
                        onClick={handleCreateProject}
                        style={{
                            background: '#3b82f6',
                            color: 'white',
                            border: 'none',
                            padding: '0.5rem 1.5rem',
                            borderRadius: '8px',
                            cursor: 'pointer',
                            fontWeight: '600'
                        }}
                    >
                        + Create Project
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
                <div className="lecturer-grid-layout">
                    {/* Left Column: Mentored Groups Monitoring Panel */}
                    <div className="lecturer-glass-card main-panel">
                        <h3 className="panel-section-title">📋 Your Mentored Capstone Groups</h3>

                        {mentoredGroups.length === 0 ? (
                            <p style={{ textAlign: 'center', padding: '2rem', color: '#6b7280' }}>
                                📭 No mentored groups assigned yet.
                            </p>
                        ) : (
                            <div className="groups-stack">
                                {mentoredGroups.map((group) => (
                                    <div key={group.id} className="group-item-card" style={{
                                        padding: '1rem',
                                        marginBottom: '0.75rem',
                                        background: '#f8fafc',
                                        borderRadius: '8px',
                                        border: '1px solid #e5e7eb'
                                    }}>
                                        <div className="group-main-details" style={{
                                            display: 'flex',
                                            justifyContent: 'space-between',
                                            alignItems: 'flex-start'
                                        }}>
                                            <div>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.25rem' }}>
                                                    <span className="group-id-badge" style={{
                                                        background: '#3b82f6',
                                                        color: 'white',
                                                        padding: '0.15rem 0.6rem',
                                                        borderRadius: '12px',
                                                        fontSize: '0.75rem',
                                                        fontWeight: '600'
                                                    }}>
                                                        {group.id}
                                                    </span>
                                                    <span style={{
                                                        fontSize: '0.7rem',
                                                        padding: '0.15rem 0.5rem',
                                                        borderRadius: '12px',
                                                        background: group.status === 'In Review' ? '#f59e0b' : '#e5e7eb',
                                                        color: group.status === 'In Review' ? 'white' : '#374151'
                                                    }}>
                                                        {group.status || 'Draft'}
                                                    </span>
                                                </div>
                                                <h4 style={{ margin: '0.25rem 0', fontSize: '1rem' }}>{group.topicName}</h4>
                                                <p style={{ margin: 0, fontSize: '0.85rem', color: '#6b7280' }}>
                                                    Leader: {group.studentLeader} • {group.memberCount} Members
                                                </p>
                                            </div>
                                        </div>

                                        <div className="group-status-actions" style={{
                                            display: 'flex',
                                            justifyContent: 'space-between',
                                            alignItems: 'center',
                                            marginTop: '0.75rem',
                                            paddingTop: '0.75rem',
                                            borderTop: '1px solid #e5e7eb'
                                        }}>
                                            <div className="status-timestamp-block">
                                                <span className={`status-pill-badge ${group.submissionStatus.toLowerCase()}`} style={{
                                                    padding: '0.15rem 0.6rem',
                                                    borderRadius: '12px',
                                                    fontSize: '0.75rem',
                                                    fontWeight: '600',
                                                    background: group.submissionStatus === 'Submitted' ? '#22c55e'
                                                        : group.submissionStatus === 'Pending' ? '#f59e0b'
                                                            : '#ef4444',
                                                    color: 'white',
                                                    marginRight: '0.5rem'
                                                }}>
                                                    {group.submissionStatus}
                                                </span>
                                                <span className="timestamp-txt" style={{ fontSize: '0.8rem', color: '#6b7280' }}>
                                                    {group.lastUpdated}
                                                </span>
                                            </div>
                                            <button
                                                className="btn-interact-action"
                                                onClick={() => handleReviewArtifacts(group.projectId)}
                                                style={{
                                                    padding: '0.4rem 1rem',
                                                    background: '#3b82f6',
                                                    color: 'white',
                                                    border: 'none',
                                                    borderRadius: '6px',
                                                    cursor: 'pointer',
                                                    fontSize: '0.85rem'
                                                }}
                                            >
                                                Review Artifacts
                                            </button>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>

                    {/* Right Column: Upcoming Evaluation Council Schedules */}
                    <div className="lecturer-glass-card sidebar-panel">
                        <h3 className="panel-section-title">📅 Upcoming Council Panels</h3>

                        {upcomingCouncils.length === 0 ? (
                            <p style={{ textAlign: 'center', padding: '2rem', color: '#6b7280' }}>
                                📭 No upcoming council assignments.
                            </p>
                        ) : (
                            <div className="councils-vertical-timeline">
                                {upcomingCouncils.map((council) => (
                                    <div key={council.id} className="council-timeline-node" style={{
                                        padding: '1rem',
                                        marginBottom: '0.75rem',
                                        background: '#f8fafc',
                                        borderRadius: '8px',
                                        border: '1px solid #e5e7eb'
                                    }}>
                                        <div className="node-time-marker" style={{
                                            display: 'flex',
                                            alignItems: 'center',
                                            gap: '0.5rem',
                                            marginBottom: '0.5rem'
                                        }}>
                                            <span className="clock-emoji" style={{ fontSize: '1.2rem' }}>⏰</span>
                                            <div>
                                                <span className="time-string" style={{ fontWeight: '600', display: 'block' }}>
                                                    {council.timeSlot}
                                                </span>
                                                <span className="room-string" style={{ fontSize: '0.85rem', color: '#6b7280' }}>
                                                    Room: {council.room}
                                                </span>
                                            </div>
                                        </div>

                                        <div className="node-body-details">
                                            <h5 style={{ margin: '0.25rem 0', fontSize: '0.95rem' }}>{council.projectTitle}</h5>
                                            <div className="role-assignment-tag" style={{
                                                margin: '0.25rem 0',
                                                fontSize: '0.85rem'
                                            }}>
                                                Assigned Role: <span style={{ fontWeight: '600', color: '#3b82f6' }}>{council.role}</span>
                                            </div>
                                            <button
                                                className="btn-grade-trigger"
                                                onClick={() => handleOpenEvaluation(council.projectId, council.projectTitle)}
                                                style={{
                                                    marginTop: '0.5rem',
                                                    padding: '0.4rem 1rem',
                                                    background: '#22c55e',
                                                    color: 'white',
                                                    border: 'none',
                                                    borderRadius: '6px',
                                                    cursor: 'pointer',
                                                    fontSize: '0.85rem',
                                                    width: '100%'
                                                }}
                                            >
                                                Open Evaluation Sheet
                                            </button>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>
                </div>
            )}

            {/* --- CREATE PROJECT MODAL --- */}
            {showCreateModal && (
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
                        <h2>Create New Project</h2>
                        <form onSubmit={handleCreateProjectSubmit}>
                            <div style={{ marginBottom: '1rem' }}>
                                <label style={{ display: 'block', marginBottom: '0.25rem', fontWeight: '500' }}>
                                    Project Title *
                                </label>
                                <input
                                    type="text"
                                    name="title"
                                    required
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
                                    Description
                                </label>
                                <textarea
                                    name="description"
                                    rows={3}
                                    style={{
                                        width: '100%',
                                        padding: '0.5rem',
                                        borderRadius: '6px',
                                        border: '1px solid #d1d5db',
                                        resize: 'vertical'
                                    }}
                                />
                            </div>

                            <div style={{ marginBottom: '1rem' }}>
                                <label style={{ display: 'block', marginBottom: '0.25rem', fontWeight: '500' }}>
                                    Team ID *
                                </label>
                                <input
                                    type="text"
                                    name="teamId"
                                    required
                                    placeholder="e.g. Team 6"
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
                                    Team Leader ID
                                </label>
                                <input
                                    type="text"
                                    name="teamLeaderId"
                                    placeholder="e.g. SE192706"
                                    style={{
                                        width: '100%',
                                        padding: '0.5rem',
                                        borderRadius: '6px',
                                        border: '1px solid #d1d5db'
                                    }}
                                />
                            </div>

                            <div style={{ display: 'flex', gap: '1rem', marginTop: '1.5rem' }}>
                                <button
                                    type="button"
                                    onClick={() => setShowCreateModal(false)}
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
                                    Create Project
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
}