// src/pages/CouncilDashboard.tsx
import { useState, useEffect, useCallback } from 'react';
import { api } from '../api';
import type { Project, ReviewSlot } from '../types';
import './CouncilDashboard.css';

interface Rebuttal {
    id: string;
    evaluationId: string;
    studentId: string;
    studentName?: string;
    projectTitle?: string;
    content: string;
    status: 'Pending' | 'Approved' | 'Rejected';
    response?: string;
    submittedAt: string;
}

interface EvaluationRecord {
    id: string;
    projectId: string;
    roundId: string;
    evaluatorId: string;
    score: number;
    feedback?: string;
    submittedAt: string;
}

export default function CouncilDashboard() {
    const [assignedProjects, setAssignedProjects] = useState<Project[]>([]);
    const [evaluationsTodo, setEvaluationsTodo] = useState<Project[]>([]);
    const [rebuttals, setRebuttals] = useState<Rebuttal[]>([]);
    const [upcomingSlots, setUpcomingSlots] = useState<ReviewSlot[]>([]);

    // Evaluation Form State
    const [selectedProjectForEval, setSelectedProjectForEval] = useState<Project | null>(null);
    const [evalScore, setEvalScore] = useState<number>(8);
    const [evalFeedback, setEvalFeedback] = useState<string>('');
    const [submittingEval, setSubmittingEval] = useState(false);

    // Rebuttal Response Form State
    const [selectedRebuttal, setSelectedRebuttal] = useState<Rebuttal | null>(null);
    const [rebuttalResponse, setRebuttalResponse] = useState<string>('');
    const [rebuttalDecision, setRebuttalDecision] = useState<'Approved' | 'Rejected'>('Approved');
    const [submittingRebuttal, setSubmittingRebuttal] = useState(false);

    const [isLoading, setIsLoading] = useState(true);
    const [isRefreshing, setIsRefreshing] = useState(false);
    const [errorMsg, setErrorMsg] = useState('');
    const [successMsg, setSuccessMsg] = useState('');

    const userId = localStorage.getItem('userId') || 'CM001';
    const fullName = localStorage.getItem('fullName') || 'Council Reviewer';

    // --- LOAD DATA ---
    const loadCouncilData = useCallback(async (showLoading: boolean = true) => {
        if (showLoading) {
            setIsLoading(true);
        } else {
            setIsRefreshing(true);
        }
        setErrorMsg('');
        setSuccessMsg('');

        try {
            // --- 1. FETCH ALL PROJECTS ---
            let projectsData: any[] = [];
            try {
                projectsData = await api.getProjects();
            } catch (err) {
                console.warn("Failed to fetch projects from API:", err);
            }

            const parsedProjects: Project[] = (Array.isArray(projectsData) ? projectsData : []).map((p: any) => ({
                id: p.id,
                title: p.title || p.topicName || '',
                team: p.teamId || p.team || p.groupCode || '',
                lecturer: p.lecturerId || p.lecturer || p.supervisorId || '',
                status: (p.status || 'Draft') as any,
                round: p.roundId || p.round || '',
                updatedAt: p.updatedAt || p.createdAt || ''
            }));

            // --- 2. CREATE PROJECTS MAP FOR QUICK LOOKUP ---
            const projectsMap = parsedProjects.reduce<Record<string, Project>>((map, project) => {
                map[project.id] = project;
                return map;
            }, {});

            // --- 3. FETCH SCHEDULE SLOTS ---
            let slotsData: any[] = [];
            try {
                slotsData = await api.getScheduleSlots();
                console.log('📅 Raw schedule slots:', slotsData);
            } catch (err) {
                console.warn("Failed to fetch slots from API:", err);
            }

            // --- 4. MAP SLOTS WITH REAL PROJECT TITLES ---
            const upcoming = (Array.isArray(slotsData) ? slotsData : [])
                .map((s: any) => {
                    const project = projectsMap[s.projectId];
                    return {
                        id: s.id,
                        projectId: s.projectId,
                        projectTitle: project?.title || 'Unknown Project',
                        room: s.room || 'Room TBD',
                        time: s.reviewDate || s.time || 'Not scheduled',
                        council: s.councilMemberIds || s.reviewerIds || [],
                        type: s.type || 'Initial Review'
                    };
                })
                .filter((s: ReviewSlot) => {
                    // Only show slots where this council member is assigned
                    return s.council.some((c: string) => c === userId || c === fullName);
                });

            console.log('📅 Mapped upcoming slots:', upcoming);
            setUpcomingSlots(upcoming);

            // --- 5. ASSIGNED PROJECTS ---
            const assignedProjectIds = new Set(upcoming.map(slot => slot.projectId));
            const assigned = parsedProjects.filter(project => assignedProjectIds.has(project.id));
            setAssignedProjects(assigned);

            // --- 6. EVALUATIONS QUEUE ---
            const todo = assigned.filter(p => p.status === 'Submitted' || p.status === 'In Review');
            setEvaluationsTodo(todo);

            // --- 7. FETCH REBUTTALS ---
            let rebuttalData: Rebuttal[] = [];
            try {
                const [pendingRebuttals, evaluations] = await Promise.all([
                    api.getPendingRebuttals(),
                    api.getAllEvaluations()
                ]);
                const evaluationsById = (evaluations as EvaluationRecord[]).reduce<Record<string, EvaluationRecord>>((map, evaluation) => {
                    map[evaluation.id] = evaluation;
                    return map;
                }, {});
                const projectsById = parsedProjects.reduce<Record<string, Project>>((map, project) => {
                    map[project.id] = project;
                    return map;
                }, {});

                rebuttalData = (pendingRebuttals as Rebuttal[]).map(rebuttal => {
                    const evaluation = evaluationsById[rebuttal.evaluationId];
                    const project = evaluation ? projectsById[evaluation.projectId] : undefined;
                    return {
                        ...rebuttal,
                        studentName: rebuttal.studentId,
                        projectTitle: project?.title || evaluation?.projectId || 'Unknown project'
                    };
                });
            } catch (err) {
                console.warn("Failed to fetch real rebuttals:", err);
            }

            setRebuttals(rebuttalData);

        } catch (err: any) {
            setErrorMsg('Failed to sync council workspace.');
            console.error(err);
        } finally {
            setIsLoading(false);
            setIsRefreshing(false);
        }
    }, [userId, fullName]);

    // --- INITIAL LOAD + AUTO-REFRESH ON FOCUS ---
    useEffect(() => {
        loadCouncilData(true);

        // Auto-refresh when page gets focus (user clicks back to tab)
        const handleFocus = () => {
            if (!isLoading && !isRefreshing) {
                loadCouncilData(false);
            }
        };
        window.addEventListener('focus', handleFocus);

        return () => {
            window.removeEventListener('focus', handleFocus);
        };
    }, [loadCouncilData]);

    // --- MANUAL REFRESH ---
    const handleRefresh = async () => {
        await loadCouncilData(false);
        if (!errorMsg) {
            setSuccessMsg('✅ Data refreshed successfully!');
            setTimeout(() => setSuccessMsg(''), 3000);
        }
    };

    // --- EVALUATION SUBMIT ---
    const handleEvaluationSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!selectedProjectForEval) return;
        if (!selectedProjectForEval.round) {
            setErrorMsg('This project does not have a review round assigned.');
            return;
        }
        setSubmittingEval(true);
        setSuccessMsg('');
        setErrorMsg('');

        try {
            await api.createEvaluation({
                projectId: selectedProjectForEval.id,
                roundId: selectedProjectForEval.round,
                evaluatorId: userId,
                score: evalScore,
                feedback: evalFeedback
            });

            setSuccessMsg(`✅ Successfully submitted evaluation for ${selectedProjectForEval.title}!`);

            // Refresh data after submit
            await loadCouncilData(false);

            setSelectedProjectForEval(null);
            setEvalFeedback('');
            setEvalScore(8);
        } catch (err: any) {
            console.error("Failed to submit evaluation:", err);
            setErrorMsg('Failed to submit evaluation. Please try again.');
        } finally {
            setSubmittingEval(false);
        }
    };

    // --- REBUTTAL SUBMIT ---
    const handleRebuttalSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!selectedRebuttal) return;
        setSubmittingRebuttal(true);
        setSuccessMsg('');
        setErrorMsg('');

        try {
            await api.respondToRebuttal(selectedRebuttal.id, rebuttalResponse);
            await api.updateRebuttalStatus(selectedRebuttal.id, rebuttalDecision);
            setSuccessMsg(`✅ Successfully responded to rebuttal from ${selectedRebuttal.studentName}!`);

            // Refresh data after submit
            await loadCouncilData(false);

            setSelectedRebuttal(null);
            setRebuttalResponse('');
            setRebuttalDecision('Approved');
        } catch (err: any) {
            console.error("Failed to respond to rebuttal:", err);
            setErrorMsg('Failed to respond to rebuttal. Please try again.');
        } finally {
            setSubmittingRebuttal(false);
        }
    };

    if (isLoading) {
        return (
            <div className="loading-container">
                <div className="spinner"></div>
                <p>Loading Council Workspace...</p>
            </div>
        );
    }

    return (
        <div className="council-dashboard-container">
            {/* --- HEADER WITH REFRESH BUTTON --- */}
            <header className="dashboard-header-block">
                <div>
                    <h2 className="dashboard-main-title">Council Member Panel</h2>
                    <p className="dashboard-subtitle">Manage assigned evaluations, student rebuttals, and upcoming schedules</p>
                </div>
                <button
                    className="refresh-btn"
                    onClick={handleRefresh}
                    disabled={isRefreshing}
                    style={{
                        padding: '0.5rem 1.5rem',
                        background: isRefreshing ? '#94a3b8' : '#3b82f6',
                        color: 'white',
                        border: 'none',
                        borderRadius: '8px',
                        cursor: isRefreshing ? 'not-allowed' : 'pointer',
                        fontWeight: '600',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '0.5rem',
                        transition: 'background 0.2s'
                    }}
                >
                    🔄 {isRefreshing ? 'Refreshing...' : 'Refresh Data'}
                </button>
            </header>

            {/* --- TOAST MESSAGES --- */}
            {successMsg && (
                <div className="toast-success-alert" style={{
                    background: '#dcfce7',
                    color: '#16a34a',
                    padding: '0.75rem 1rem',
                    borderRadius: '8px',
                    marginBottom: '1rem',
                    border: '1px solid #bbf7d0'
                }}>
                    ✅ {successMsg}
                </div>
            )}
            {errorMsg && (
                <div className="toast-error-alert" style={{
                    background: '#fee2e2',
                    color: '#dc2626',
                    padding: '0.75rem 1rem',
                    borderRadius: '8px',
                    marginBottom: '1rem',
                    border: '1px solid #fecaca'
                }}>
                    ❌ {errorMsg}
                </div>
            )}

            {/* --- MAIN GRID --- */}
            <div className="dashboard-grid-layout">
                {/* LEFT: Tasks Queue */}
                <div className="dashboard-primary-section">

                    {/* Evaluations Queue */}
                    <section className="glass-card-section">
                        <h3 className="section-title-label">📋 Evaluations Queue</h3>
                        {evaluationsTodo.length === 0 ? (
                            <p className="no-records-message">All caught up! No evaluations waiting for your review.</p>
                        ) : (
                            <div className="evaluation-queue-list">
                                {evaluationsTodo.map(project => (
                                    <div key={project.id} className="evaluation-queue-item">
                                        <div className="item-details">
                                            <h4>{project.title}</h4>
                                            <p>Team: <strong>{project.team}</strong> | Mentor: {project.lecturer}</p>
                                        </div>
                                        <button
                                            className="action-btn-primary"
                                            onClick={() => setSelectedProjectForEval(project)}
                                        >
                                            Evaluate
                                        </button>
                                    </div>
                                ))}
                            </div>
                        )}
                    </section>

                    {/* Submit Evaluation Panel */}
                    {selectedProjectForEval && (
                        <section className="glass-card-section evaluation-form-active">
                            <h3 className="section-title-label">⭐ Evaluate Project: {selectedProjectForEval.title}</h3>
                            <form onSubmit={handleEvaluationSubmit} className="feedback-scoring-form">
                                <div className="form-input-group">
                                    <label htmlFor="score-range-slider">Score (0 - 10): <span className="score-val-badge">{evalScore}</span></label>
                                    <input
                                        type="range"
                                        id="score-range-slider"
                                        min="0"
                                        max="10"
                                        step="0.5"
                                        value={evalScore}
                                        onChange={(e) => setEvalScore(parseFloat(e.target.value))}
                                    />
                                </div>
                                <div className="form-input-group">
                                    <label htmlFor="evaluation-feedback-text">Feedback Comments</label>
                                    <textarea
                                        id="evaluation-feedback-text"
                                        rows={4}
                                        placeholder="Add constructive comments, strengths, weaknesses..."
                                        value={evalFeedback}
                                        onChange={(e) => setEvalFeedback(e.target.value)}
                                        required
                                    />
                                </div>
                                <div className="form-button-actions">
                                    <button
                                        type="button"
                                        className="btn-cancel"
                                        onClick={() => setSelectedProjectForEval(null)}
                                    >
                                        Cancel
                                    </button>
                                    <button
                                        type="submit"
                                        className="btn-submit"
                                        disabled={submittingEval}
                                    >
                                        {submittingEval ? 'Submitting...' : 'Submit Evaluation'}
                                    </button>
                                </div>
                            </form>
                        </section>
                    )}

                    {/* Rebuttals Awaiting Response */}
                    <section className="glass-card-section">
                        <h3 className="section-title-label">💬 Rebuttals Awaiting Response</h3>
                        {rebuttals.length === 0 ? (
                            <p className="no-records-message">No pending rebuttals found.</p>
                        ) : (
                            <div className="rebuttals-list">
                                {rebuttals.map(rebuttal => (
                                    <div key={rebuttal.id} className="rebuttal-item-card">
                                        <div className="rebuttal-header">
                                            <span className="student-badge">From: {rebuttal.studentName}</span>
                                            <span className="project-tag">{rebuttal.projectTitle}</span>
                                        </div>
                                        <p className="rebuttal-content-text">"{rebuttal.content}"</p>

                                        {rebuttal.response ? (
                                            <div className="rebuttal-response-display">
                                                <strong>Your response:</strong>
                                                <p>{rebuttal.response}</p>
                                            </div>
                                        ) : (
                                            <button
                                                className="action-btn-secondary"
                                                onClick={() => {
                                                    setSelectedRebuttal(rebuttal);
                                                    setRebuttalResponse('');
                                                    setRebuttalDecision('Approved');
                                                }}
                                            >
                                                Respond to Rebuttal
                                            </button>
                                        )}
                                    </div>
                                ))}
                            </div>
                        )}
                    </section>

                    {/* Rebuttal Response Panel */}
                    {selectedRebuttal && (
                        <section className="glass-card-section evaluation-form-active">
                            <h3 className="section-title-label">Respond to Rebuttal from {selectedRebuttal.studentName}</h3>
                            <form onSubmit={handleRebuttalSubmit} className="feedback-scoring-form">
                                <div className="form-input-group">
                                    <label htmlFor="rebuttal-decision-select">Decision</label>
                                    <select
                                        id="rebuttal-decision-select"
                                        value={rebuttalDecision}
                                        onChange={(e) => setRebuttalDecision(e.target.value as 'Approved' | 'Rejected')}
                                        required
                                    >
                                        <option value="Approved">Approve rebuttal</option>
                                        <option value="Rejected">Reject rebuttal</option>
                                    </select>
                                </div>
                                <div className="form-input-group">
                                    <label htmlFor="rebuttal-response-text">Your Response Message</label>
                                    <textarea
                                        id="rebuttal-response-text"
                                        rows={3}
                                        placeholder="Type your rebuttal response decision here..."
                                        value={rebuttalResponse}
                                        onChange={(e) => setRebuttalResponse(e.target.value)}
                                        required
                                    />
                                </div>
                                <div className="form-button-actions">
                                    <button
                                        type="button"
                                        className="btn-cancel"
                                        onClick={() => {
                                            setSelectedRebuttal(null);
                                            setRebuttalResponse('');
                                            setRebuttalDecision('Approved');
                                        }}
                                    >
                                        Cancel
                                    </button>
                                    <button
                                        type="submit"
                                        className="btn-submit"
                                        disabled={submittingRebuttal}
                                    >
                                        {submittingRebuttal ? 'Submitting...' : 'Send Response'}
                                    </button>
                                </div>
                            </form>
                        </section>
                    )}
                </div>

                {/* RIGHT: Assigned Projects & Schedules */}
                <div className="dashboard-secondary-section">

                    {/* Assigned Projects */}
                    <section className="glass-card-section">
                        <h3 className="section-title-label">🛡️ Assigned Projects</h3>
                        <div className="assigned-projects-list">
                            {assignedProjects.length === 0 ? (
                                <p className="no-records-message">No assigned projects found.</p>
                            ) : (
                                assignedProjects.map(proj => (
                                    <div key={proj.id} className="project-compact-card">
                                        <div className="card-top">
                                            <h4>{proj.title}</h4>
                                            <span className={`status-badge-indicator ${proj.status.toLowerCase().replace(' ', '-')}`}>
                                                {proj.status}
                                            </span>
                                        </div>
                                        <div className="card-bottom">
                                            <p>Team: {proj.team}</p>
                                            <p>Round: {proj.round}</p>
                                        </div>
                                    </div>
                                ))
                            )}
                        </div>
                    </section>

                    {/* Upcoming Review Slots */}
                    <section className="glass-card-section">
                        <h3 className="section-title-label">📅 Upcoming Review Slots</h3>
                        {upcomingSlots.length === 0 ? (
                            <p className="no-records-message">No upcoming reviews scheduled.</p>
                        ) : (
                            <div className="slots-timeline-list">
                                {upcomingSlots.map(slot => (
                                    <div key={slot.id} className="slot-timeline-card">
                                        <div className="timeline-badge-time">
                                            {new Date(slot.time).toLocaleString()}
                                        </div>
                                        <div className="slot-card-body">
                                            <h4>{slot.projectTitle}</h4>
                                            <p>Room: <strong>{slot.room}</strong> | Type: {slot.type}</p>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        )}
                    </section>
                </div>
            </div>
        </div>
    );
}