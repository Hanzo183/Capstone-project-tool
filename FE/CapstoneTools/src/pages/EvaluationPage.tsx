// src/pages/EvaluationPage.tsx
import { useState, useEffect } from 'react';
import { api } from '../api';
import './EvaluationPage.css';

interface Evaluation {
    id: string;
    projectId: string;
    roundId: string;
    evaluatorId: string;
    score: number;
    feedback: string;
    submittedAt: string;
    evaluatorName?: string;
}

interface Rebuttal {
    id: string;
    evaluationId: string;
    studentId: string;
    content: string;
    status: 'Pending' | 'Approved' | 'Rejected';
    response?: string;
    submittedAt: string;
    reviewedAt?: string;
}

export default function EvaluationPage() {
    const [evaluations, setEvaluations] = useState<Evaluation[]>([]);
    const [rebuttals, setRebuttals] = useState<Rebuttal[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [errorMsg, setErrorMsg] = useState('');
    const [showRebuttalForm, setShowRebuttalForm] = useState(false);
    const [rebuttalContent, setRebuttalContent] = useState('');
    const [selectedEvaluationId, setSelectedEvaluationId] = useState<string>('');
    const [isSubmitting, setIsSubmitting] = useState(false);

    const userId = localStorage.getItem('userId') || '';
    const userRole = localStorage.getItem('role') || 'Student';

    useEffect(() => {
        const loadEvaluationData = async () => {
            try {
                setIsLoading(true);

                const projects = await api.getProjects();
                const userProjects = Array.isArray(projects) ? projects : [];
                const visibleProjects = userProjects;

                if (visibleProjects.length === 0) {
                    setEvaluations([]);
                    setRebuttals([]);
                    return;
                }

                const project = visibleProjects[0];

                const evalData = await api.getEvaluations(project.id);
                setEvaluations(evalData);

                const rebuttalPromises = evalData.map((evalItem: Evaluation) =>
                    api.getRebuttals(evalItem.id).catch(() => [])
                );
                const rebuttalResults = await Promise.all(rebuttalPromises);
                const allRebuttals = rebuttalResults.flat();
                setRebuttals(allRebuttals);
            } catch (err) {
                console.error('Failed to load evaluations:', err);
                setErrorMsg('Could not load evaluation data.');
            } finally {
                setIsLoading(false);
            }
        };

        loadEvaluationData();
    }, []);

    // --- SUBMIT REBUTTAL ---
    const handleSubmitRebuttal = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!rebuttalContent.trim() || !selectedEvaluationId) return;

        setIsSubmitting(true);

        try {
            const newRebuttal = await api.createRebuttal({
                evaluationId: selectedEvaluationId,
                studentId: userId,
                content: rebuttalContent
            });

            setRebuttals([newRebuttal, ...rebuttals]);
            setShowRebuttalForm(false);
            setRebuttalContent('');
            setSelectedEvaluationId('');
            alert('✅ Rebuttal submitted successfully!');
        } catch (err) {
            console.error('Failed to submit rebuttal:', err);
            alert('❌ Failed to submit rebuttal. Please try again.');
        } finally {
            setIsSubmitting(false);
        }
    };

    // --- UPDATE REBUTTAL STATUS (Lecturer/Council) ---
    const handleUpdateRebuttalStatus = async (rebuttalId: string, status: 'Approved' | 'Rejected') => {
        if (!window.confirm(`Are you sure you want to ${status} this rebuttal?`)) return;

        try {
            await api.updateRebuttalStatus(rebuttalId, status);
            alert(`✅ Rebuttal ${status}!`);

            // Refresh rebuttals
            const updatedRebuttals = rebuttals.map(r =>
                r.id === rebuttalId ? { ...r, status } : r
            );
            setRebuttals(updatedRebuttals);
        } catch (err) {
            console.error('Failed to update rebuttal status:', err);
            alert('❌ Failed to update rebuttal status.');
        }
    };

    // --- RESPOND TO REBUTTAL (Lecturer/Council) ---
    const handleRespondToRebuttal = async (rebuttalId: string, responseText: string) => {
        if (!responseText.trim()) {
            alert('Please enter a response.');
            return;
        }

        try {
            await api.respondToRebuttal(rebuttalId, responseText);
            alert('✅ Response submitted successfully!');

            // Refresh rebuttals
            const updatedRebuttals = rebuttals.map(r =>
                r.id === rebuttalId ? { ...r, response: responseText } : r
            );
            setRebuttals(updatedRebuttals);
        } catch (err) {
            console.error('Failed to respond to rebuttal:', err);
            alert('❌ Failed to submit response.');
        }
    };

    // --- GET EVALUATOR NAME ---
    const getEvaluatorName = (evaluatorId: string) => {
        const names: Record<string, string> = {
            'CM001': 'Council Member 001',
            'SE192879': 'Tran Tuan Minh',
            'SE192737': 'Luong Pham Binh Minh'
        };
        return names[evaluatorId] || evaluatorId;
    };

    // --- GET STATUS COLOR ---
    const getStatusColor = (status: string) => {
        switch (status?.toLowerCase()) {
            case 'pending': return '#f59e0b';
            case 'approved': return '#22c55e';
            case 'rejected': return '#ef4444';
            default: return '#6b7280';
        }
    };

    // --- GET STATUS ICON ---
    const getStatusIcon = (status: string) => {
        switch (status?.toLowerCase()) {
            case 'pending': return '⏳';
            case 'approved': return '✅';
            case 'rejected': return '❌';
            default: return '📝';
        }
    };

    return (
        <div className="evaluation-page">
            <header className="evaluation-header">
                <h2>📊 Evaluation & Feedback</h2>
                <p>View your project scores, feedback, and submit rebuttals if needed.</p>
            </header>

            {errorMsg && (
                <div className="error-banner">{errorMsg}</div>
            )}

            {isLoading ? (
                <div className="loading-state">⏳ Loading evaluations...</div>
            ) : (
                <div className="evaluation-grid">
                    {/* Main Evaluations List */}
                    <div className="evaluations-section">
                        <h3>Your Evaluations</h3>

                        {evaluations.length === 0 ? (
                            <div className="empty-state">
                                <p>📭 No evaluations yet. Check back after your review.</p>
                            </div>
                        ) : (
                            evaluations.map((evalItem) => {
                                const hasRebuttal = rebuttals.some(r => r.evaluationId === evalItem.id);
                                const rebuttal = rebuttals.find(r => r.evaluationId === evalItem.id);

                                return (
                                    <div key={evalItem.id} className="evaluation-card">
                                        <div className="evaluation-header-row">
                                            <div className="evaluator-info">
                                                <span className="evaluator-name">
                                                    👤 {getEvaluatorName(evalItem.evaluatorId)}
                                                </span>
                                                <span className="evaluation-date">
                                                    {new Date(evalItem.submittedAt).toLocaleString()}
                                                </span>
                                            </div>
                                            <div className="score-display">
                                                <span className="score-value">{evalItem.score}</span>
                                                <span className="score-max">/ 10</span>
                                            </div>
                                        </div>

                                        <div className="feedback-section">
                                            <p className="feedback-text">"{evalItem.feedback}"</p>
                                        </div>

                                        {/* ============================================
                                            REBUTTAL SECTION - STUDENT VIEW
                                        ============================================ */}
                                        {userRole === 'Student' && (
                                            <>
                                                {hasRebuttal ? (
                                                    <div className="rebuttal-section">
                                                        <div className="rebuttal-content">
                                                            <div className="rebuttal-header">
                                                                <strong>Your Rebuttal:</strong>
                                                                <span
                                                                    className={`rebuttal-status ${rebuttal?.status?.toLowerCase()}`}
                                                                    style={{
                                                                        padding: '0.15rem 0.6rem',
                                                                        borderRadius: '12px',
                                                                        fontSize: '0.75rem',
                                                                        fontWeight: '600',
                                                                        background: getStatusColor(rebuttal?.status || 'pending'),
                                                                        color: 'white'
                                                                    }}
                                                                >
                                                                    {getStatusIcon(rebuttal?.status || 'pending')} {rebuttal?.status || 'Pending'}
                                                                </span>
                                                            </div>
                                                            <p className="rebuttal-text">{rebuttal?.content}</p>
                                                            <p className="rebuttal-meta">
                                                                Submitted: {new Date(rebuttal?.submittedAt || '').toLocaleString()}
                                                            </p>

                                                            {rebuttal?.response && (
                                                                <div className="rebuttal-response">
                                                                    <strong>Response from Council:</strong>
                                                                    <p className="response-text">{rebuttal.response}</p>
                                                                    {rebuttal.reviewedAt && (
                                                                        <p className="response-meta">
                                                                            Reviewed: {new Date(rebuttal.reviewedAt).toLocaleString()}
                                                                        </p>
                                                                    )}
                                                                </div>
                                                            )}

                                                            {rebuttal?.status === 'Pending' && (
                                                                <div className="pending-message">
                                                                    ⏳ Waiting for council response...
                                                                </div>
                                                            )}
                                                        </div>
                                                    </div>
                                                ) : (
                                                    <button
                                                        className="btn-rebuttal"
                                                        onClick={() => {
                                                            setSelectedEvaluationId(evalItem.id);
                                                            setShowRebuttalForm(true);
                                                        }}
                                                    >
                                                        📝 Submit Rebuttal
                                                    </button>
                                                )}
                                            </>
                                        )}

                                        {/* ============================================
                                            REBUTTAL SECTION - LECTURER/COUNCIL VIEW
                                        ============================================ */}
                                        {(userRole === 'Lecturer' || userRole === 'CouncilMember') && (
                                            <>
                                                {hasRebuttal ? (
                                                    <div className="rebuttal-section lecturer-view">
                                                        <div className="rebuttal-content">
                                                            <div className="rebuttal-header">
                                                                <strong>Student Rebuttal:</strong>
                                                                <span
                                                                    className={`rebuttal-status ${rebuttal?.status?.toLowerCase()}`}
                                                                    style={{
                                                                        padding: '0.15rem 0.6rem',
                                                                        borderRadius: '12px',
                                                                        fontSize: '0.75rem',
                                                                        fontWeight: '600',
                                                                        background: getStatusColor(rebuttal?.status || 'pending'),
                                                                        color: 'white'
                                                                    }}
                                                                >
                                                                    {getStatusIcon(rebuttal?.status || 'pending')} {rebuttal?.status || 'Pending'}
                                                                </span>
                                                            </div>
                                                            <p className="rebuttal-text">
                                                                <strong>Student:</strong> {rebuttal?.studentId}
                                                            </p>
                                                            <p className="rebuttal-text">
                                                                <strong>Content:</strong> {rebuttal?.content}
                                                            </p>
                                                            <p className="rebuttal-meta">
                                                                Submitted: {new Date(rebuttal?.submittedAt || '').toLocaleString()}
                                                            </p>

                                                            {rebuttal?.response && (
                                                                <div className="rebuttal-response">
                                                                    <strong>Your Response:</strong>
                                                                    <p className="response-text">{rebuttal.response}</p>
                                                                </div>
                                                            )}

                                                            {rebuttal?.status === 'Pending' && (
                                                                <div className="rebuttal-actions">
                                                                    <textarea
                                                                        className="response-textarea"
                                                                        placeholder="Write your response to the student..."
                                                                        rows={3}
                                                                        style={{
                                                                            width: '100%',
                                                                            padding: '0.5rem',
                                                                            borderRadius: '6px',
                                                                            border: '1px solid #d1d5db',
                                                                            resize: 'vertical',
                                                                            fontFamily: 'inherit',
                                                                            marginBottom: '0.5rem'
                                                                        }}
                                                                    />
                                                                    <div className="action-buttons">
                                                                        <button
                                                                            className="btn-approve"
                                                                            onClick={(e) => {
                                                                                const textarea = e.currentTarget.parentElement?.previousElementSibling as HTMLTextAreaElement;
                                                                                const responseText = textarea?.value || '';
                                                                                handleRespondToRebuttal(rebuttal.id, responseText);
                                                                                handleUpdateRebuttalStatus(rebuttal.id, 'Approved');
                                                                            }}
                                                                            style={{
                                                                                padding: '0.3rem 1rem',
                                                                                background: '#22c55e',
                                                                                color: 'white',
                                                                                border: 'none',
                                                                                borderRadius: '4px',
                                                                                cursor: 'pointer',
                                                                                marginRight: '0.5rem'
                                                                            }}
                                                                        >
                                                                            ✅ Approve
                                                                        </button>
                                                                        <button
                                                                            className="btn-reject"
                                                                            onClick={(e) => {
                                                                                const textarea = e.currentTarget.parentElement?.previousElementSibling as HTMLTextAreaElement;
                                                                                const responseText = textarea?.value || '';
                                                                                handleRespondToRebuttal(rebuttal.id, responseText);
                                                                                handleUpdateRebuttalStatus(rebuttal.id, 'Rejected');
                                                                            }}
                                                                            style={{
                                                                                padding: '0.3rem 1rem',
                                                                                background: '#ef4444',
                                                                                color: 'white',
                                                                                border: 'none',
                                                                                borderRadius: '4px',
                                                                                cursor: 'pointer'
                                                                            }}
                                                                        >
                                                                            ❌ Reject
                                                                        </button>
                                                                    </div>
                                                                </div>
                                                            )}
                                                        </div>
                                                    </div>
                                                ) : (
                                                    <div className="no-rebuttal-message">
                                                        <span style={{ color: '#6b7280', fontSize: '0.85rem' }}>
                                                            No rebuttal submitted for this evaluation.
                                                        </span>
                                                    </div>
                                                )}
                                            </>
                                        )}
                                    </div>
                                );
                            })
                        )}
                    </div>

                    {/* Rebuttal Form Modal (Student) */}
                    {showRebuttalForm && (
                        <div className="modal-overlay">
                            <div className="modal-content">
                                <h3>📝 Submit Rebuttal</h3>
                                <p>Explain why you disagree with the evaluation and provide additional context.</p>
                                <form onSubmit={handleSubmitRebuttal}>
                                    <textarea
                                        rows={6}
                                        placeholder="Write your rebuttal here..."
                                        value={rebuttalContent}
                                        onChange={(e) => setRebuttalContent(e.target.value)}
                                        required
                                        disabled={isSubmitting}
                                        style={{
                                            width: '100%',
                                            padding: '0.75rem',
                                            borderRadius: '8px',
                                            border: '2px solid #e2e8f0',
                                            resize: 'vertical',
                                            fontFamily: 'inherit',
                                            fontSize: '1rem'
                                        }}
                                    />
                                    <div className="modal-actions">
                                        <button
                                            type="button"
                                            onClick={() => {
                                                setShowRebuttalForm(false);
                                                setRebuttalContent('');
                                                setSelectedEvaluationId('');
                                            }}
                                            className="btn-cancel"
                                            disabled={isSubmitting}
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
                                            className="btn-submit"
                                            disabled={isSubmitting}
                                            style={{
                                                padding: '0.5rem 1.5rem',
                                                background: '#3b82f6',
                                                color: 'white',
                                                border: 'none',
                                                borderRadius: '6px',
                                                cursor: isSubmitting ? 'not-allowed' : 'pointer',
                                                opacity: isSubmitting ? 0.6 : 1
                                            }}
                                        >
                                            {isSubmitting ? '⏳ Submitting...' : 'Submit Rebuttal'}
                                        </button>
                                    </div>
                                </form>
                            </div>
                        </div>
                    )}

                    {/* Summary Stats */}
                    {evaluations.length > 0 && (
                        <div className="summary-section">
                            <h3>📊 Summary</h3>
                            <div className="stats-grid">
                                <div className="stat-item">
                                    <span className="stat-label">Average Score</span>
                                    <span className="stat-value">
                                        {(evaluations.reduce((sum, e) => sum + e.score, 0) / evaluations.length).toFixed(1)}
                                    </span>
                                </div>
                                <div className="stat-item">
                                    <span className="stat-label">Total Evaluations</span>
                                    <span className="stat-value">{evaluations.length}</span>
                                </div>
                                <div className="stat-item">
                                    <span className="stat-label">Rebuttals</span>
                                    <span className="stat-value">{rebuttals.length}</span>
                                </div>
                                {rebuttals.length > 0 && (
                                    <>
                                        <div className="stat-item">
                                            <span className="stat-label">Pending</span>
                                            <span className="stat-value" style={{ color: '#f59e0b' }}>
                                                {rebuttals.filter(r => r.status === 'Pending').length}
                                            </span>
                                        </div>
                                        <div className="stat-item">
                                            <span className="stat-label">Approved</span>
                                            <span className="stat-value" style={{ color: '#22c55e' }}>
                                                {rebuttals.filter(r => r.status === 'Approved').length}
                                            </span>
                                        </div>
                                        <div className="stat-item">
                                            <span className="stat-label">Rejected</span>
                                            <span className="stat-value" style={{ color: '#ef4444' }}>
                                                {rebuttals.filter(r => r.status === 'Rejected').length}
                                            </span>
                                        </div>
                                    </>
                                )}
                            </div>
                        </div>
                    )}
                </div>
            )}
        </div>
    );
}
