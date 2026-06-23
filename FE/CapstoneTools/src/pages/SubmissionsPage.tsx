// src/pages/SubmissionsPage.tsx
import React, { useState } from 'react';
import './SubmissionsPage.css';

interface SubmissionItem {
    id: string;
    version: number;
    fileName: string;
    submittedAt: string;
    submittedBy: string;
    status: 'Evaluated' | 'Pending Review';
    score?: number;
    feedback?: string;
}

export default function SubmissionsPage() {
    const role = localStorage.getItem('role');

    // Mock State for Student Submissions History
    const [submissions, setSubmissions] = useState<SubmissionItem[]>([
        { id: 'S2', version: 2, fileName: 'architecture_diagram.pdf', submittedAt: '2026-06-19 14:30', submittedBy: 'SE192706', status: 'Evaluated', score: 8.5, feedback: 'Excellent infrastructure breakdown. Add more gateway details.' },
        { id: 'S1', version: 1, fileName: 'requirements_doc.docx', submittedAt: '2026-06-15 09:15', submittedBy: 'SE192737', status: 'Evaluated', score: 8.0, feedback: 'Functional baseline looks solid.' }
    ]);

    // Mock State for Lecturer Grading Panel Selection
    const [selectedGroup, setSelectedGroup] = useState('Group 6 - Microservices E-Commerce');
    const [gradeScore, setGradeScore] = useState('');
    const [evaluationFeedback, setEvaluationFeedback] = useState('');

    // Handle Drag & Drop Upload Mock Execution
    const handleFileUploadMock = (e: React.FormEvent) => {
        e.preventDefault();
        const targetFile = "revised_system_proposal.pdf";
        const nextVersion = submissions.length + 1;

        const newUpload: SubmissionItem = {
            id: `S${nextVersion}`,
            version: nextVersion,
            fileName: targetFile,
            submittedAt: new Date().toISOString().replace('T', ' ').substring(0, 16),
            submittedBy: 'SE192706',
            status: 'Pending Review'
        };
        setSubmissions([newUpload, ...submissions]);
    };

    // Submit Lecturer Grading Feedback Form
    const handleGradeSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        alert(`Evaluation Saved for ${selectedGroup}\nScore: ${gradeScore}/10\nFeedback committed successfully!`);
        setGradeScore('');
        setEvaluationFeedback('');
    };

    return (
        <div className="submissions-page-viewport">
            <div className="view-header-block">
                <h2 className="view-title">Artifact Repository & Submissions Portal</h2>
                <p className="view-subtitle">Review file histories, coordinate feedback channels, and manage versioned artifacts.</p>
            </div>

            {/* --- STUDENT VIEW MATRIX --- */}
            {role === 'Student' && (
                <div className="submissions-grid-container student-mode">

                    {/* Left Column: Drag and Drop Upload Area */}
                    <div className="portal-glass-card">
                        <h3 className="card-section-title">Upload New Version Artifact</h3>
                        <form onSubmit={handleFileUploadMock} className="drag-drop-zone-box">
                            <div className="zone-dashed-boundary">
                                <span className="cloud-icon">📤</span>
                                <p className="primary-prompt-text">Drag & drop your milestone deliverables here</p>
                                <p className="secondary-format-text">Supports PDF, DOCX, ZIP files up to 50MB</p>
                                <button type="submit" className="action-browse-trigger">Simulate New Version Upload</button>
                            </div>
                        </form>
                    </div>

                    {/* Right Column: Execution Audit History Records */}
                    <div className="portal-glass-card">
                        <h3 className="card-section-title">Submission History Logs</h3>
                        <div className="history-timeline-stack">
                            {submissions.map((item) => (
                                <div key={item.id} className="history-log-item">
                                    <div className="log-top-header">
                                        <div className="file-identity">
                                            <span className="version-pill-tag">v{item.version}</span>
                                            <strong className="filename-txt">{item.fileName}</strong>
                                        </div>
                                        <span className={`eval-status-indicator ${item.status.toLowerCase().replace(' ', '-')}`}>
                                            {item.status}
                                        </span>
                                    </div>

                                    <p className="metadata-attribution-line">Uploaded at {item.submittedAt} by {item.submittedBy}</p>

                                    {item.score !== undefined && (
                                        <div className="feedback-result-callout">
                                            <div className="score-summary-line">Assigned Score: <strong>{item.score} / 10</strong></div>
                                            <p className="feedback-text-body">"{item.feedback}"</p>
                                        </div>
                                    )}
                                </div>
                            ))}
                        </div>
                    </div>
                </div>
            )}

            {/* --- LECTURER VIEW MATRIX --- */}
            {role === 'Lecturer' && (
                <div className="submissions-grid-container lecturer-mode">

                    {/* Left Column: Team Artifact Selector Grid */}
                    <div className="portal-glass-card">
                        <h3 className="card-section-title">Incoming Student Deliverables</h3>
                        <div className="selector-control-header">
                            <label htmlFor="group-select">Active Cohort Target:</label>
                            <select
                                id="group-select"
                                value={selectedGroup}
                                onChange={(e) => setSelectedGroup(e.target.value)}
                                className="styled-form-dropdown"
                            >
                                <option value="Group 6 - Microservices E-Commerce">Group 6 - Microservices E-Commerce App</option>
                                <option value="Group 4 - Smart Agriculture System">Group 4 - Smart Agriculture System</option>
                            </select>
                        </div>

                        <div className="lecturer-artifact-file-row">
                            <div className="artifact-meta-card">
                                <div className="file-icon-block">📄</div>
                                <div className="file-text-block">
                                    <strong>architecture_diagram.pdf</strong>
                                    <span>Version 2 • Submitted by SE192706</span>
                                </div>
                                <a href="#download-mock" onClick={() => alert("Downloading file buffer context link...")} className="btn-download-link">Download File</a>
                            </div>
                        </div>
                    </div>

                    {/* Right Column: Evaluation Sheet Form Controller */}
                    <div className="portal-glass-card">
                        <h3 className="card-section-title">Perform Evaluation Matrix</h3>
                        <form onSubmit={handleGradeSubmit} className="evaluation-input-form">
                            <div className="form-input-group">
                                <label className="input-field-label">Quantitative Milestone Score (0.0 - 10.0)</label>
                                <input
                                    type="number"
                                    step="0.1"
                                    min="0"
                                    max="10"
                                    required
                                    placeholder="e.g. 8.5"
                                    value={gradeScore}
                                    onChange={(e) => setGradeScore(e.target.value)}
                                    className="styled-form-textbox"
                                />
                            </div>

                            <div className="form-input-group">
                                <label className="input-field-label">Qualitative Structural Critique & Feedback</label>
                                <textarea
                                    rows={5}
                                    required
                                    placeholder="Enter detailed optimization suggestions or rebuttal prerequisites..."
                                    value={evaluationFeedback}
                                    onChange={(e) => setEvaluationFeedback(e.target.value)}
                                    className="styled-form-textarea"
                                />
                            </div>

                            <button type="submit" className="btn-submit-evaluation-trigger">Commit Evaluation Scores</button>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
}