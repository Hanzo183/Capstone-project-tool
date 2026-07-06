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
    const [projectId, setProjectId] = useState<string>('');
    const [isLoading, setIsLoading] = useState(true);
    const [errorMsg, setErrorMsg] = useState('');
    const [showRebuttalForm, setShowRebuttalForm] = useState(false);
    const [rebuttalContent, setRebuttalContent] = useState('');
    const [selectedEvaluationId, setSelectedEvaluationId] = useState<string>('');

    const userId = localStorage.getItem('userId') || '';
    const userRole = localStorage.getItem('role') || 'Student';

    useEffect(() => {
        const loadEvaluationData = async () => {
            try {
                setIsLoading(true);

                // Get user's project
                const projects = await api.getProjects();
                if (projects && projects.length > 0) {
                    const project = projects[0];
                    setProjectId(project.id);

                    // Get evaluations for this project
                    const evalData = await api.getEvaluations(project.id);
                    setEvaluations(evalData);

                    // Get rebuttals (if any)
                    // You might need a different endpoint for this
                    // For now, we'll check if rebuttals exist for each evaluation
                    const rebuttalPromises = evalData.map((evalItem: Evaluation) =>
                        api.getRebuttals(evalItem.id).catch(() => [])
                    );
                    const rebuttalResults = await Promise.all(rebuttalPromises);
                    const allRebuttals = rebuttalResults.flat();
                    setRebuttals(allRebuttals);
                }
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
        }
    };

    // --- GET EVALUATOR NAME (mock for now) ---
    const getEvaluatorName = (evaluatorId: string) => {
        // You could fetch user details from Identity Service
        const names: Record<string, string> = {
            'CM001': 'Council Member 001',
            'SE192879': 'Tran Tuan Minh',
            'SE192737': 'Luong Pham Binh Minh'
        };
        return names[evaluatorId] || evaluatorId;
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

                                        {/* Rebuttal Section */}
                                        {hasRebuttal ? (
                                            <div className="rebuttal-section">
                                                <div className="rebuttal-content">
                                                    <strong>Your Rebuttal:</strong>
                                                    <p>{rebuttal?.content}</p>
                                                    <span className={`rebuttal-status ${rebuttal?.status?.toLowerCase()}`}>
                                                        Status: {rebuttal?.status || 'Pending'}
                                                    </span>
                                                    {rebuttal?.response && (
                                                        <div className="rebuttal-response">
                                                            <strong>Response:</strong>
                                                            <p>{rebuttal.response}</p>
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
                                    </div>
                                );
                            })
                        )}
                    </div>

                    {/* Rebuttal Form Modal */}
                    {showRebuttalForm && (
                        <div className="modal-overlay">
                            <div className="modal-content">
                                <h3>Submit Rebuttal</h3>
                                <p>Explain why you disagree with the evaluation and provide additional context.</p>
                                <form onSubmit={handleSubmitRebuttal}>
                                    <textarea
                                        rows={6}
                                        placeholder="Write your rebuttal here..."
                                        value={rebuttalContent}
                                        onChange={(e) => setRebuttalContent(e.target.value)}
                                        required
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
                                        >
                                            Cancel
                                        </button>
                                        <button type="submit" className="btn-submit">
                                            Submit Rebuttal
                                        </button>
                                    </div>
                                </form>
                            </div>
                        </div>
                    )}

                    {/* Summary Stats */}
                    {evaluations.length > 0 && (
                        <div className="summary-section">
                            <h3>Summary</h3>
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
                            </div>
                        </div>
                    )}
                </div>
            )}
        </div>
    );
}