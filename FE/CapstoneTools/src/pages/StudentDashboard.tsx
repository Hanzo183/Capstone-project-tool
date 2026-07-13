// src/pages/StudentDashboard.tsx
import { useState, useEffect } from 'react';
import { api } from '../api';
import type { Project, Submission, ReviewSlot, ScheduleEvent, ProjectStatus } from '../types';
import './StudentDashboard.css';

interface BackendSubmission {
    id: string;
    version?: number;
    fileUrl?: string;
    fileName?: string;      // ← IMPORTANT: Use fileName from database
    submittedAt?: string;
    submittedBy?: string;
    status?: string;
}

export default function StudentDashboard() {
    const [currentProject, setCurrentProject] = useState<Project>({
        id: '...',
        title: 'Loading project context...',
        team: '...',
        lecturer: '...',
        status: 'Draft' as ProjectStatus,
        round: '...',
        score: 0,
        updatedAt: '...'
    });

    const [submissions, setSubmissions] = useState<Submission[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [isUploading, setIsUploading] = useState(false);
    const [errorMsg, setErrorMsg] = useState('');

    useEffect(() => {
        const loadDashboardData = async () => {
            try {
                const projectData = await api.getProjects();
                const userProjects = Array.isArray(projectData) ? projectData : [];
                const visibleProjects = userProjects;

                if (visibleProjects.length > 0) {
                    const activeRes = visibleProjects[0];
                    const projectId = activeRes.id;

                    setCurrentProject({
                        id: projectId || 'N/A',
                        title: activeRes.topicName || activeRes.title || 'Untitled Topic',
                        team: activeRes.groupCode || activeRes.teamName || 'No Group',
                        lecturer: activeRes.supervisorId || activeRes.lecturerId || 'Unassigned Mentor',
                        status: (activeRes.status || 'Draft') as ProjectStatus,
                        round: 'Spring 2026 - Iteration 1',
                        score: 8.5,
                        updatedAt: activeRes.createdAt ? new Date(activeRes.createdAt).toLocaleDateString() : 'Recent'
                    });

                    if (projectId) {
                        const submissionData = await api.getSubmissions(projectId);
                        
                        // ✅ FIXED: Map submissions with proper fileName from database
                        const mappedSubmissions: Submission[] = submissionData.map((sub: BackendSubmission) => {
                            let subStatus: "Pending" | "Evaluated" = "Pending";
                            if (sub.status === "Evaluated") {
                                subStatus = "Evaluated";
                            }

                            return {
                                id: sub.id,
                                projectId: projectId,
                                version: sub.version || 1,
                                // ✅ FIXED: Use fileName from database, not artifactUrl
                                fileName: sub.fileName || 'unknown.pdf',
                                submittedAt: sub.submittedAt ? new Date(sub.submittedAt).toLocaleString() : 'Unknown date',
                                submittedBy: sub.submittedBy || 'Team Member',
                                status: subStatus
                            };
                        });
                        
                        setSubmissions(mappedSubmissions);
                    }
                } else {
                    setErrorMsg('No active project records found for your account entry.');
                }
            } catch (err) {
                console.error("API Connection Failure:", err);
                setErrorMsg('Could not sync workspace with Project Service.');
            } finally {
                setIsLoading(false);
            }
        };

        loadDashboardData();
    }, []);

    const handleFileUpload = async (event: React.ChangeEvent<HTMLInputElement>) => {
        const file = event.target.files?.[0];
        if (!file || currentProject.id === '...') return;

        setIsUploading(true);
        setErrorMsg('');

        const formData = new FormData();
        formData.append('file', file);

        try {
            const responseData = await api.uploadSubmission(currentProject.id, formData);
            
            setSubmissions(prev => [
                {
                    id: responseData.id || `S${Date.now()}`,
                    projectId: currentProject.id,
                    version: responseData.version || (prev.length + 1),
                    // ✅ FIXED: Use the actual file name
                    fileName: file.name,
                    submittedAt: 'Just now',
                    submittedBy: 'Me',
                    status: 'Pending'
                },
                ...prev
            ]);
        } catch  {
            setErrorMsg('Artifact transmission failed. Please try again.');
        } finally {
            setIsUploading(false);
        }
    };

    const scheduleTimeline: ScheduleEvent[] = [
        { id: 't1', title: 'Proposal Submission', date: 'May 15, 2026', status: 'Completed' },
        { id: 't2', title: 'Iteration 1 Review', date: 'June 25, 2026', status: 'Current' },
        { id: 't3', title: 'Final Defense', date: 'July 20, 2026', status: 'Upcoming' },
    ];

    const upcomingSlot: ReviewSlot = {
        id: 'R1',
        projectId: currentProject.id,
        projectTitle: currentProject.title,
        room: 'Alpha 105',
        time: '2026-06-25 10:00 AM',
        council: ['Dr. Nguyen Van A', 'Dr. Tran Thi B', 'Mr. Le Van C'],
        type: 'Initial Review'
    };

    // ✅ FIXED: Download function for dashboard
    const handleDownload = async (storedName: string, displayName: string) => {
        if (!currentProject.id || currentProject.id === '...') {
            alert('No project selected.');
            return;
        }

        try {
            const blob = await api.downloadFile(currentProject.id, storedName);
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = displayName || storedName;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            window.URL.revokeObjectURL(url);
        } catch (err) {
            console.error('Download failed:', err);
            alert('Failed to download file. Please try again.');
        }
    };

    return (
        <div className="dashboard-container">
            <header className="dashboard-header">
                <h2>Student Workspace</h2>
                <p className="welcome-text">Track your capstone progress, submissions, and upcoming review slots.</p>
            </header>

            <div className="dashboard-grid">
                {/* 1. Project Overview Card */}
                <div className="glass-card project-overview-card">
                    <h3 className="card-title">Project Overview</h3>
                    {errorMsg && <div className="error-message-banner" style={{ color: '#ef4444', marginBottom: '1rem', fontSize: '0.9rem' }}>{errorMsg}</div>}

                    <div className={`project-details ${isLoading ? 'loading-fade' : ''}`}>
                        <div className="detail-row">
                            <span className="label">Title</span>
                            <strong className="value highlight">{currentProject.title}</strong>
                        </div>
                        <div className="detail-row">
                            <span className="label">Team</span>
                            <span className="value">{currentProject.team}</span>
                        </div>
                        <div className="detail-row">
                            <span className="label">Mentor</span>
                            <span className="value">{currentProject.lecturer}</span>
                        </div>
                        <div className="detail-row">
                            <span className="label">Current Round</span>
                            <span className="value">{currentProject.round}</span>
                        </div>
                        <div className="detail-row">
                            <span className="label">Status</span>
                            <span className={`status-badge ${currentProject.status.toLowerCase().replace(/\s+/g, '-')}`}>
                                {currentProject.status}
                            </span>
                        </div>
                    </div>
                </div>

                {/* 2. Schedule Timeline Card */}
                <div className="glass-card schedule-card">
                    <h3 className="card-title">Milestone Timeline</h3>
                    <div className="timeline-container">
                        {scheduleTimeline.map((event) => (
                            <div key={event.id} className={`timeline-node ${event.status.toLowerCase()}`}>
                                <div className="node-marker"></div>
                                <div className="node-content">
                                    <h4>{event.title}</h4>
                                    <p>{event.date}</p>
                                </div>
                                <span className="node-status">{event.status}</span>
                            </div>
                        ))}
                    </div>
                </div>

                {/* 3. Upcoming Review Slot Card */}
                <div className="glass-card upcoming-review-card">
                    <h3 className="card-title">Next Council Review</h3>
                    <div className="slot-highlight">
                        <div className="slot-datetime">
                            <span className="icon">📅</span>
                            <strong>{upcomingSlot.time}</strong>
                        </div>
                        <div className="slot-location">
                            <span className="icon">📍</span>
                            <span>Room {upcomingSlot.room}</span>
                        </div>
                        <div className="council-list">
                            <span className="label">Council Members:</span>
                            <ul>
                                {upcomingSlot.council.map((member, idx) => (
                                    <li key={idx}>{member}</li>
                                ))}
                            </ul>
                        </div>
                    </div>
                </div>

                {/* 4. Recent Submissions Panel */}
                <div className="glass-card submissions-card">
                    <div className="card-header-flex">
                        <h3 className="card-title">Artifact Repository</h3>
                    </div>

                    <div className="submission-workspace">
                        <div className="quick-upload-zone">
                            <span className="upload-icon">☁️</span>
                            <p>{isUploading ? 'Uploading artifact file...' : 'Upload project documentation (.pdf, .docx)'}</p>
                            <label className="btn-secondary" style={{ display: 'inline-block', cursor: 'pointer' }}>
                                Browse Files
                                <input
                                    type="file"
                                    style={{ display: 'none' }}
                                    onChange={handleFileUpload}
                                    disabled={isUploading || currentProject.id === '...'}
                                    accept=".pdf,.docx,.doc,.zip"
                                />
                            </label>
                        </div>

                        <h4 className="history-title">Version History</h4>
                        <div className="submission-list">
                            {isLoading ? (
                                <p style={{ fontSize: '0.8rem', color: '#64748b', textAlign: 'center', marginTop: '1rem' }}>
                                    Loading submissions...
                                </p>
                            ) : submissions.length === 0 ? (
                                <p style={{ fontSize: '0.8rem', color: '#64748b', textAlign: 'center', marginTop: '1rem' }}>
                                    No artifacts uploaded yet.
                                </p>
                            ) : (
                                submissions.map((sub) => (
                                    <div key={sub.id} className="submission-item">
                                        <div className="submission-header">
                                            <div className="submission-info">
                                                {/* ✅ FIXED: Now shows actual file name from database */}
                                                <p className="file-name" title={sub.fileName}>
                                                    {sub.fileName}
                                                </p>
                                                <p className="file-meta">By {sub.submittedBy} • {sub.submittedAt}</p>
                                            </div>
                                            <span className="version-badge">v{sub.version}</span>
                                        </div>
                                        <div className="submission-footer">
                                            <span className={`status-badge-small ${sub.status.toLowerCase()}`}>
                                                {sub.status}
                                            </span>
                                            <button 
                                                className="text-button download-btn"
                                                onClick={() => handleDownload(sub.fileName, sub.fileName)}
                                            >
                                                Download
                                            </button>
                                        </div>
                                    </div>
                                ))
                            )}
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
}
