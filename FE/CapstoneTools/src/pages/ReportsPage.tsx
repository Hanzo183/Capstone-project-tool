// src/pages/ReportsPage.tsx
import { useState, useEffect } from 'react';
import { api } from '../api';
import './ReportsPage.css';

interface ReviewRound {
    id: string;
    name: string;
    startDate: string;
    endDate: string;
    status: string;
    createdBy: string;
    createdAt: string;
}

interface Project {
    id: string;
    title: string;
    teamId: string;
    status: string;
    lecturerId: string;
    roundId?: string;
}

interface Submission {
    id: string;
    projectId: string;
    fileName: string;
    version: number;
    submittedAt: string;
    submittedBy: string;
}

interface Evaluation {
    id: string;
    projectId: string;
    roundId: string;
    evaluatorId: string;
    score: number;
    feedback: string;
    submittedAt: string;
}

interface ReportData {
    roundId: string;
    roundName: string;
    totalProjects: number;
    evaluatedProjects: number;
    pendingProjects: number;
    averageScore: number;
    totalSubmissions: number;
    evaluations: Array<{
        projectId: string;
        projectTitle: string;
        teamId: string;
        evaluatorId: string;
        score: number;
        feedback: string;
        submittedAt: string;
    }>;
    topPerformers: Array<{
        projectId: string;
        projectTitle: string;
        teamId: string;
        averageScore: number;
    }>;
}

export default function ReportsPage() {
    const [rounds, setRounds] = useState<ReviewRound[]>([]);
    const [selectedRoundId, setSelectedRoundId] = useState<string>('');
    const [reportData, setReportData] = useState<ReportData | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [isLoadingReport, setIsLoadingReport] = useState(false);
    const [errorMsg, setErrorMsg] = useState('');
    const [isDownloading, setIsDownloading] = useState(false);

    const role = localStorage.getItem('role');
    const isAdminOrLecturer = role === 'Admin' || role === 'Lecturer';

    // --- LOAD ROUNDS ---
    useEffect(() => {
        const loadRounds = async () => {
            try {
                setIsLoading(true);
                const data = await api.getReviewRounds();
                setRounds(data || []);

                if (data && data.length > 0) {
                    setSelectedRoundId(data[0].id);
                }
            } catch (err) {
                console.error('Failed to load rounds:', err);
                setErrorMsg('Could not load review rounds.');
            } finally {
                setIsLoading(false);
            }
        };

        if (isAdminOrLecturer) {
            loadRounds();
        }
    }, [isAdminOrLecturer]);

    // --- ✅ FIXED: BUILD REPORT FROM EXISTING DATA ---
    useEffect(() => {
        const buildReport = async () => {
            if (!selectedRoundId) return;

            try {
                setIsLoadingReport(true);
                setErrorMsg('');

                // 1. Get all projects
                const allProjects = await api.getProjects();

                // 2. Filter projects by selected round
                const roundProjects = allProjects.filter(
                    (p: Project) => p.roundId === selectedRoundId
                );

                // 3. Get all submissions for these projects
                let allSubmissions: Submission[] = [];
                for (const project of roundProjects) {
                    try {
                        const subs = await api.getSubmissions(project.id);
                        allSubmissions = [...allSubmissions, ...subs];
                    } catch  {
                        console.warn(`No submissions for project ${project.id}`);
                    }
                }

                // 4. Get all evaluations for these projects
                let allEvaluations: Evaluation[] = [];
                for (const project of roundProjects) {
                    try {
                        const evals = await api.getEvaluations(project.id);
                        allEvaluations = [...allEvaluations, ...evals];
                    } catch  {
                        console.warn(`No evaluations for project ${project.id}`);
                    }
                }

                // 5. Calculate statistics
                const totalProjects = roundProjects.length;
                const totalSubmissions = allSubmissions.length;
                const evaluatedProjects = allEvaluations.length > 0 ? roundProjects.length : 0;
                const pendingProjects = Math.max(0, totalProjects - evaluatedProjects);

                // Calculate average score
                let averageScore = 0;
                if (allEvaluations.length > 0) {
                    const totalScore = allEvaluations.reduce((sum: number, e: Evaluation) => sum + e.score, 0);
                    averageScore = totalScore / allEvaluations.length;
                }

                // 6. Build evaluation details
                const evaluations = allEvaluations.map((evalItem: Evaluation) => {
                    const project = roundProjects.find((p: Project) => p.id === evalItem.projectId);
                    return {
                        projectId: evalItem.projectId,
                        projectTitle: project?.title || 'Unknown Project',
                        teamId: project?.teamId || 'N/A',
                        evaluatorId: evalItem.evaluatorId,
                        score: evalItem.score,
                        feedback: evalItem.feedback || '—',
                        submittedAt: evalItem.submittedAt
                    };
                });

                // 7. Build top performers
                const projectScores: Record<string, { total: number; count: number; title: string; teamId: string }> = {};
                allEvaluations.forEach((evalItem: Evaluation) => {
                    if (!projectScores[evalItem.projectId]) {
                        const project = roundProjects.find((p: Project) => p.id === evalItem.projectId);
                        projectScores[evalItem.projectId] = {
                            total: 0,
                            count: 0,
                            title: project?.title || 'Unknown Project',
                            teamId: project?.teamId || 'N/A'
                        };
                    }
                    projectScores[evalItem.projectId].total += evalItem.score;
                    projectScores[evalItem.projectId].count += 1;
                });

                const topPerformers = Object.entries(projectScores).map(([projectId, data]) => ({
                    projectId,
                    projectTitle: data.title,
                    teamId: data.teamId,
                    averageScore: data.total / data.count
                })).sort((a, b) => b.averageScore - a.averageScore);

                // 8. Build final report
                const round = rounds.find(r => r.id === selectedRoundId);
                const report: ReportData = {
                    roundId: selectedRoundId,
                    roundName: round?.name || 'Unknown Round',
                    totalProjects,
                    evaluatedProjects,
                    pendingProjects,
                    averageScore,
                    totalSubmissions,
                    evaluations,
                    topPerformers
                };

                console.log('📊 Report built:', report);
                setReportData(report);

            } catch (err) {
                console.error('Failed to build report:', err);
                setErrorMsg('Could not build report data.');
                setReportData(null);
            } finally {
                setIsLoadingReport(false);
            }
        };

        if (selectedRoundId) {
            buildReport();
        }
    }, [selectedRoundId, rounds]);

    // --- HANDLE ROUND CHANGE ---
    const handleRoundChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
        setSelectedRoundId(e.target.value);
    };

    // --- HANDLE DOWNLOAD PDF ---
    const handleDownloadPdf = async () => {
        if (!selectedRoundId) return;

        try {
            setIsDownloading(true);
            const blob = await api.downloadReportPdf(selectedRoundId);

            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            const round = rounds.find(r => r.id === selectedRoundId);
            a.download = `report_${round?.name || selectedRoundId}.pdf`;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            window.URL.revokeObjectURL(url);

            alert('✅ Report downloaded successfully!');
        } catch (err) {
            console.error('Failed to download report:', err);
            alert('❌ Failed to download report. Please try again.');
        } finally {
            setIsDownloading(false);
        }
    };

    // --- HANDLE EXPORT CSV ---
    const handleExportCsv = () => {
        if (!reportData) return;

        try {
            const headers = ['Project ID', 'Project Title', 'Team', 'Score', 'Feedback', 'Evaluator', 'Date'];
            const rows = reportData.evaluations.map((evalItem) => [
                evalItem.projectId,
                evalItem.projectTitle,
                evalItem.teamId,
                evalItem.score,
                `"${evalItem.feedback.replace(/"/g, '""')}"`,
                evalItem.evaluatorId,
                new Date(evalItem.submittedAt).toLocaleDateString()
            ]);

            const csvContent = [
                headers.join(','),
                ...rows.map(row => row.join(','))
            ].join('\n');

            const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            const round = rounds.find(r => r.id === selectedRoundId);
            a.download = `report_${round?.name || selectedRoundId}.csv`;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            window.URL.revokeObjectURL(url);

            alert('✅ CSV exported successfully!');
        } catch (err) {
            console.error('Failed to export CSV:', err);
            alert('❌ Failed to export CSV.');
        }
    };

    // Redirect if not authorized
    if (!isAdminOrLecturer) {
        return (
            <div className="reports-page">
                <div className="unauthorized">
                    <h2>🔒 Access Denied</h2>
                    <p>You do not have permission to view this page.</p>
                </div>
            </div>
        );
    }

    return (
        <div className="reports-page">
            <header className="reports-header">
                <div>
                    <h2>📊 Reports & Analytics</h2>
                    <p>View evaluation reports for review rounds and export data.</p>
                </div>
            </header>

            {errorMsg && (
                <div className="error-banner">
                    ❌ {errorMsg}
                </div>
            )}

            {/* Round Selector */}
            <div className="round-selector">
                <label htmlFor="round-select">Select Review Round:</label>
                {isLoading ? (
                    <span>Loading rounds...</span>
                ) : (
                    <select
                        id="round-select"
                        value={selectedRoundId}
                        onChange={handleRoundChange}
                        disabled={isLoadingReport}
                    >
                        {rounds.length === 0 ? (
                            <option value="">No rounds available</option>
                        ) : (
                            rounds.map((round) => (
                                <option key={round.id} value={round.id}>
                                    {round.name} ({round.status})
                                </option>
                            ))
                        )}
                    </select>
                )}
            </div>

            {/* Report Content */}
            {isLoadingReport ? (
                <div className="loading-state">⏳ Loading report data...</div>
            ) : reportData ? (
                <>
                    {/* ✅ FIXED: Summary Cards - Now showing real data */}
                    <div className="summary-cards">
                        <div className="summary-card">
                            <span className="summary-label">Total Projects</span>
                            <span className="summary-value">{reportData.totalProjects}</span>
                        </div>
                        <div className="summary-card green">
                            <span className="summary-label">Evaluated</span>
                            <span className="summary-value">{reportData.evaluatedProjects}</span>
                        </div>
                        <div className="summary-card yellow">
                            <span className="summary-label">Pending</span>
                            <span className="summary-value">{reportData.pendingProjects}</span>
                        </div>
                        <div className="summary-card blue">
                            <span className="summary-label">Average Score</span>
                            <span className="summary-value">{reportData.averageScore.toFixed(1)}</span>
                        </div>
                        <div className="summary-card purple">
                            <span className="summary-label">Total Submissions</span>
                            <span className="summary-value">{reportData.totalSubmissions}</span>
                        </div>
                    </div>

                    {/* Top Performers */}
                    {reportData.topPerformers && reportData.topPerformers.length > 0 && (
                        <div className="top-performers">
                            <h3>🏆 Top Performers</h3>
                            <div className="performer-list">
                                {reportData.topPerformers.map((performer, index) => (
                                    <div key={performer.projectId} className="performer-item">
                                        <span className="performer-rank">#{index + 1}</span>
                                        <div className="performer-info">
                                            <span className="performer-title">{performer.projectTitle}</span>
                                            <span className="performer-team">{performer.teamId}</span>
                                        </div>
                                        <span className="performer-score">{performer.averageScore.toFixed(1)} / 10</span>
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}

                    {/* Evaluation Details Table */}
                    <div className="evaluation-table-section">
                        <div className="table-header">
                            <h3>📋 Evaluation Details</h3>
                            <div className="table-actions">
                                <button
                                    className="btn-download-pdf"
                                    onClick={handleDownloadPdf}
                                    disabled={isDownloading}
                                >
                                    {isDownloading ? '⏳ Downloading...' : '📄 Download PDF'}
                                </button>
                                <button
                                    className="btn-export-csv"
                                    onClick={handleExportCsv}
                                >
                                    📊 Export CSV
                                </button>
                            </div>
                        </div>

                        {reportData.evaluations.length === 0 ? (
                            <p className="no-data">No evaluations found for this round.</p>
                        ) : (
                            <div className="table-responsive">
                                <table className="evaluation-table">
                                    <thead>
                                        <tr>
                                            <th>Project</th>
                                            <th>Team</th>
                                            <th>Score</th>
                                            <th>Feedback</th>
                                            <th>Evaluator</th>
                                            <th>Date</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {reportData.evaluations.map((evalItem, index) => (
                                            <tr key={`${evalItem.projectId}-${index}`}>
                                                <td className="project-cell">
                                                    <span className="project-title">{evalItem.projectTitle}</span>
                                                    <span className="project-id">{evalItem.projectId}</span>
                                                </td>
                                                <td>{evalItem.teamId}</td>
                                                <td>
                                                    <span className={`score-badge ${evalItem.score >= 7 ? 'high' : evalItem.score >= 5 ? 'medium' : 'low'}`}>
                                                        {evalItem.score}
                                                    </span>
                                                </td>
                                                <td className="feedback-cell">{evalItem.feedback || '—'}</td>
                                                <td>{evalItem.evaluatorId}</td>
                                                <td>{new Date(evalItem.submittedAt).toLocaleDateString()}</td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        )}
                    </div>
                </>
            ) : (
                <div className="no-report">
                    <p>📭 No report data available for this round.</p>
                    <p className="no-report-sub">Select a different round or wait for evaluations to be submitted.</p>
                </div>
            )}
        </div>
    );
}
