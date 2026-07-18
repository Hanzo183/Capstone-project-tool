// src/pages/SubmissionsPage.tsx
import { useState, useEffect } from 'react';
import { api } from '../api';
import './SubmissionsPage.css';

interface SubmissionItem {
    id: string;
    version: number;
    fileName: string;
    storedName: string;
    fileUrl: string;
    submittedAt: string;
    submittedBy: string;
    status: 'Evaluated' | 'Pending Review';
    score?: number;
    feedback?: string;
    projectId?: string;
}

interface BackendSubmission {
    id: string;
    projectId: string;
    fileUrl: string;
    fileName: string;
    version: number;
    submittedAt: string;
    submittedBy: string;
    status?: string;
}

interface Project {
    id: string;
    title: string;
    teamId: string;
    teamLeaderId?: string;
    lecturerId: string;
    status: string;
    roundId?: string;
}

export default function SubmissionsPage() {
    const role = localStorage.getItem('role');
    const userId = localStorage.getItem('userId') || '';
    const [projectId, setProjectId] = useState<string>('');
    const [submissions, setSubmissions] = useState<SubmissionItem[]>([]);
    const [projects, setProjects] = useState<Project[]>([]);
    const [selectedProjectId, setSelectedProjectId] = useState<string>('');
    const [isLoading, setIsLoading] = useState(true);
    const [errorMsg, setErrorMsg] = useState('');
    const [isUploading, setIsUploading] = useState(false);

    // Lecturer evaluation state
    const [gradeScore, setGradeScore] = useState('');
    const [evaluationFeedback, setEvaluationFeedback] = useState('');
    const [isSubmittingEval, setIsSubmittingEval] = useState(false);

    // --- ENHANCED FILTER/SORT/SEARCH/PAGINATION/BULK DELETE STATE ---
    const [searchQuery, setSearchQuery] = useState('');
    const [statusFilter, setStatusFilter] = useState<'All' | 'Pending Review' | 'Evaluated'>('All');
    const [sortBy, setSortBy] = useState<'newest' | 'oldest'>('newest');
    const [currentPage, setCurrentPage] = useState(1);
    const [itemsPerPage, setItemsPerPage] = useState(5);
    const [selectedSubIds, setSelectedSubIds] = useState<string[]>([]);

    useEffect(() => {
        setCurrentPage(1);
    }, [searchQuery, statusFilter, sortBy, itemsPerPage]);

    const handleBulkDelete = () => {
        if (selectedSubIds.length === 0) return;
        if (window.confirm(`Are you sure you want to delete ${selectedSubIds.length} submission(s)?`)) {
            setSubmissions(prev => prev.filter(sub => !selectedSubIds.includes(sub.id)));
            setSelectedSubIds([]);
            alert('✅ Successfully deleted selected submissions.');
        }
    };

    const handleSelectAll = (items: SubmissionItem[]) => {
        const itemIds = items.map(sub => sub.id);
        const allSelected = itemIds.every(id => selectedSubIds.includes(id));
        if (allSelected) {
            setSelectedSubIds(prev => prev.filter(id => !itemIds.includes(id)));
        } else {
            setSelectedSubIds(prev => Array.from(new Set([...prev, ...itemIds])));
        }
    };

    const handleSelectToggle = (id: string) => {
        setSelectedSubIds(prev =>
            prev.includes(id) ? prev.filter(item => item !== id) : [...prev, id]
        );
    };

    // Processed Submissions
    const filteredSubmissions = submissions
        .filter(sub => {
            const matchesSearch = sub.fileName.toLowerCase().includes(searchQuery.toLowerCase());
            const matchesStatus = statusFilter === 'All' || sub.status === statusFilter;
            return matchesSearch && matchesStatus;
        })
        .sort((a, b) => {
            const dateA = new Date(a.submittedAt).getTime();
            const dateB = new Date(b.submittedAt).getTime();
            return sortBy === 'newest' ? dateB - dateA : dateA - dateB;
        });

    const totalItems = filteredSubmissions.length;
    const totalPages = Math.ceil(totalItems / itemsPerPage) || 1;
    const paginatedSubmissions = filteredSubmissions.slice(
        (currentPage - 1) * itemsPerPage,
        currentPage * itemsPerPage
    );

    // --- LOAD SUBMISSIONS ---
    useEffect(() => {
        const loadData = async () => {
            try {
                setIsLoading(true);
                setErrorMsg('');

                // Get all projects
                const projectsData = await api.getProjects();
                setProjects(projectsData || []);

                // For Lecturer: filter projects they supervise
                if (role === 'Lecturer') {
                    const lecturerProjects = projectsData.filter(
                        (p: Project) => p.lecturerId === userId
                    );
                    setProjects(lecturerProjects);

                    if (lecturerProjects.length > 0) {
                        setSelectedProjectId(lecturerProjects[0].id);
                        await loadSubmissions(lecturerProjects[0].id);
                    }
                } else {
                    // For Student: get their project
                    const studentProject = projectsData[0];
                    if (studentProject) {
                        setSelectedProjectId(studentProject.id);
                        setProjectId(studentProject.id);
                        await loadSubmissions(studentProject.id);
                    }
                }
            } catch (err) {
                console.error('Failed to load data:', err);
                setErrorMsg('Could not load data. Please try again.');
            } finally {
                setIsLoading(false);
            }
        };

        loadData();
    }, [role, userId]);

    // --- LOAD SUBMISSIONS FOR A PROJECT ---
    const loadSubmissions = async (projectId: string) => {
        try {
            const data = await api.getSubmissions(projectId);
            const mappedSubmissions: SubmissionItem[] = data.map((sub: BackendSubmission) => ({
                id: sub.id,
                projectId: projectId,
                version: sub.version || 1,
                fileName: sub.fileName || 'unknown.pdf',
                storedName: sub.fileUrl ? sub.fileUrl.split('/').pop() || sub.fileName : sub.fileName,
                fileUrl: sub.fileUrl || '',
                submittedAt: sub.submittedAt ? new Date(sub.submittedAt).toLocaleString() : 'Unknown date',
                submittedBy: sub.submittedBy || 'Team Member',
                status: sub.status === 'Evaluated' ? 'Evaluated' : 'Pending Review',
            }));
            setSubmissions(mappedSubmissions);
            setProjectId(projectId);
        } catch (err) {
            console.error('Failed to load submissions:', err);
            setSubmissions([]);
        }
    };

    // --- HANDLE PROJECT SWITCH (Lecturer) ---
    const handleProjectChange = async (e: React.ChangeEvent<HTMLSelectElement>) => {
        const newProjectId = e.target.value;
        setSelectedProjectId(newProjectId);
        await loadSubmissions(newProjectId);
    };

    // --- UPLOAD FILE ---
    const handleFileUpload = async (event: React.ChangeEvent<HTMLInputElement>) => {
        const file = event.target.files?.[0];
        if (!file || !projectId) return;

        const allowedExtensions = ['.pdf', '.doc', '.docx', '.zip'];
        const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
        if (!allowedExtensions.includes(extension)) {
            setErrorMsg('Only PDF, Word, or ZIP files are allowed.');
            event.target.value = '';
            return;
        }

        setIsUploading(true);
        setErrorMsg('');

        const formData = new FormData();
        formData.append('file', file);

        try {
            const response = await api.uploadSubmission(projectId, formData);

            const newSub: SubmissionItem = {
                id: response.id || `S${Date.now()}`,
                version: response.version || submissions.length + 1,
                fileName: response.fileName || file.name,
                storedName: response.fileUrl ? response.fileUrl.split('/').pop() || file.name : file.name,
                fileUrl: response.fileUrl || '',
                submittedAt: new Date().toLocaleString(),
                submittedBy: 'Me',
                status: 'Pending Review',
                projectId: projectId
            };

            setSubmissions([newSub, ...submissions]);
            alert('✅ File uploaded successfully!');
        } catch (err) {
            console.error('Upload failed:', err);
            setErrorMsg('Upload failed. Please try again.');
        } finally {
            setIsUploading(false);
            event.target.value = '';
        }
    };

    // --- DOWNLOAD FILE ---
    const handleDownload = async (storedName: string, displayName: string) => {
        if (!projectId) {
            alert('No project selected.');
            return;
        }

        try {
            const blob = await api.downloadFile(projectId, storedName);
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
            alert('❌ Failed to download file.');
        }
    };

    // --- ✅ FIXED: REAL EVALUATION SUBMIT ---
    const handleEvaluationSubmit = async (e: React.FormEvent) => {
        e.preventDefault();

        if (!selectedProjectId) {
            alert('Please select a project first.');
            return;
        }

        if (!gradeScore || !evaluationFeedback) {
            alert('Please fill in both score and feedback.');
            return;
        }

        const numericScore = parseFloat(gradeScore);
        if (Number.isNaN(numericScore) || numericScore < 0 || numericScore > 10) {
            alert('Score must be between 0 and 10.');
            return;
        }

        setIsSubmittingEval(true);

        try {
            // Get the current round for this project
            const project = projects.find(p => p.id === selectedProjectId);
            const roundId = project?.roundId || 'RND-2025A'; // Fallback to default

            await api.createEvaluation({
                projectId: selectedProjectId,
                roundId: roundId,
                evaluatorId: userId,
                score: numericScore,
                feedback: evaluationFeedback,
                studentId: project?.teamLeaderId
            });

            alert('✅ Evaluation submitted successfully!');
            setGradeScore('');
            setEvaluationFeedback('');

            // Refresh submissions to show updated status
            await loadSubmissions(selectedProjectId);
        } catch (err) {
            console.error('Evaluation failed:', err);
            alert('❌ Failed to submit evaluation. Please try again.');
        } finally {
            setIsSubmittingEval(false);
        }
    };

    // --- GET PROJECT NAME ---
    const getProjectName = (id: string) => {
        const project = projects.find(p => p.id === id);
        return project?.title || 'Unknown Project';
    };

    const renderToolbarAndControls = (currentItemsList: SubmissionItem[]) => {
        const allSelected = currentItemsList.length > 0 && currentItemsList.every(sub => selectedSubIds.includes(sub.id));
        return (
            <div className="submissions-toolbar-container">
                <div className="toolbar-search-row">
                    <div className="search-input-wrapper">
                        <span className="search-icon">🔍</span>
                        <input
                            type="text"
                            placeholder="Search by filename..."
                            value={searchQuery}
                            onChange={(e) => setSearchQuery(e.target.value)}
                            className="search-textbox"
                        />
                        {searchQuery && (
                            <button onClick={() => setSearchQuery('')} className="clear-search-btn">✕</button>
                        )}
                    </div>
                </div>

                <div className="toolbar-filter-sort-row">
                    <div className="filter-group">
                        <label>Status:</label>
                        <select
                            value={statusFilter}
                            onChange={(e) => setStatusFilter(e.target.value as any)}
                            className="filter-select"
                        >
                            <option value="All">All Statuses</option>
                            <option value="Pending Review">Pending Review</option>
                            <option value="Evaluated">Evaluated</option>
                        </select>
                    </div>

                    <div className="sort-group">
                        <label>Sort:</label>
                        <select
                            value={sortBy}
                            onChange={(e) => setSortBy(e.target.value as any)}
                            className="sort-select"
                        >
                            <option value="newest">Newest First</option>
                            <option value="oldest">Oldest First</option>
                        </select>
                    </div>

                    <div className="page-size-group">
                        <label>Show:</label>
                        <select
                            value={itemsPerPage}
                            onChange={(e) => setItemsPerPage(Number(e.target.value))}
                            className="pagesize-select"
                        >
                            <option value={3}>3 per page</option>
                            <option value={5}>5 per page</option>
                            <option value={10}>10 per page</option>
                            <option value={20}>20 per page</option>
                        </select>
                    </div>
                </div>

                {/* Bulk Actions */}
                <div className="bulk-actions-wrapper">
                    <div className="select-all-checkbox-label">
                        <input
                            type="checkbox"
                            checked={allSelected}
                            onChange={() => handleSelectAll(currentItemsList)}
                            id="selectAllCheckbox"
                        />
                        <label htmlFor="selectAllCheckbox" style={{ cursor: 'pointer' }}>
                            {allSelected ? 'Deselect All' : 'Select All on Page'}
                        </label>
                    </div>

                    {selectedSubIds.length > 0 && (
                        <button
                            onClick={handleBulkDelete}
                            className="btn-bulk-delete-action"
                        >
                            🗑️ Delete Selected ({selectedSubIds.length})
                        </button>
                    )}
                </div>
            </div>
        );
    };

    const renderPagination = () => {
        if (totalPages <= 1) return null;
        return (
            <div className="pagination-wrapper">
                <button
                    disabled={currentPage === 1}
                    onClick={() => setCurrentPage(p => Math.max(1, p - 1))}
                    className="pagination-btn arrow-btn"
                >
                    ◀ Prev
                </button>
                <div className="pagination-pages-list">
                    {Array.from({ length: totalPages }, (_, idx) => idx + 1).map(page => (
                        <button
                            key={page}
                            onClick={() => setCurrentPage(page)}
                            className={`pagination-btn page-number-btn ${currentPage === page ? 'active' : ''}`}
                        >
                            {page}
                        </button>
                    ))}
                </div>
                <button
                    disabled={currentPage === totalPages}
                    onClick={() => setCurrentPage(p => Math.min(totalPages, p + 1))}
                    className="pagination-btn arrow-btn"
                >
                    Next ▶
                </button>
            </div>
        );
    };

    return (
        <div className="submissions-page-viewport">
            <div className="view-header-block">
                <h2 className="view-title">📁 Artifact Repository & Submissions Portal</h2>
                <p className="view-subtitle">Review file histories, coordinate feedback channels, and manage versioned artifacts.</p>
            </div>

            {errorMsg && (
                <div className="error-banner" style={{
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

            {/* --- STUDENT VIEW --- */}
            {role === 'Student' && (
                <div className="submissions-grid-container student-mode">
                    {/* Upload Section */}
                    <div className="portal-glass-card">
                        <h3 className="card-section-title">📤 Upload New Version Artifact</h3>
                        <div className="drag-drop-zone-box">
                            <div className="zone-dashed-boundary" style={{
                                border: '2px dashed #d1d5db',
                                borderRadius: '12px',
                                padding: '2rem',
                                textAlign: 'center',
                                background: '#fafafa'
                            }}>
                                <span className="cloud-icon" style={{ fontSize: '3rem' }}>📤</span>
                                <p className="primary-prompt-text" style={{ fontSize: '1.1rem', fontWeight: '500', margin: '0.5rem 0' }}>
                                    {isUploading ? '⏳ Uploading...' : 'Drag & drop your milestone deliverables here'}
                                </p>
                                <p className="secondary-format-text" style={{ color: '#6b7280', fontSize: '0.9rem' }}>
                                    Supports PDF, DOCX, ZIP files up to 50MB
                                </p>
                                <label style={{
                                    cursor: isUploading ? 'not-allowed' : 'pointer',
                                    display: 'inline-block',
                                    padding: '0.6rem 1.5rem',
                                    background: isUploading ? '#9ca3af' : '#3b82f6',
                                    color: 'white',
                                    borderRadius: '8px',
                                    border: 'none',
                                    fontWeight: '500',
                                    marginTop: '0.5rem',
                                    opacity: isUploading ? 0.6 : 1
                                }}>
                                    {isUploading ? 'Uploading...' : 'Browse Files'}
                                    <input
                                        type="file"
                                        style={{ display: 'none' }}
                                        onChange={handleFileUpload}
                                        disabled={isUploading || !projectId}
                                        accept=".pdf,.docx,.doc,.zip"
                                    />
                                </label>
                            </div>
                        </div>
                    </div>

                    {/* Submission History */}
                    <div className="portal-glass-card">
                        <h3 className="card-section-title">📋 Submission History Logs</h3>
                        {isLoading ? (
                            <p style={{ textAlign: 'center', padding: '2rem', color: '#6b7280' }}>⏳ Loading submissions...</p>
                        ) : submissions.length === 0 ? (
                            <p style={{ textAlign: 'center', padding: '2rem', color: '#6b7280' }}>📭 No submissions yet.</p>
                        ) : (
                            <>
                                {renderToolbarAndControls(paginatedSubmissions)}
                                {paginatedSubmissions.length === 0 ? (
                                    <p style={{ textAlign: 'center', padding: '2rem', color: '#6b7280' }}>🔍 No submissions match your search/filter criteria.</p>
                                ) : (
                                    <div className="history-timeline-stack">
                                        {paginatedSubmissions.map((item) => (
                                            <div key={item.id} className="history-log-item" style={{
                                                padding: '1rem',
                                                marginBottom: '0.75rem',
                                                background: '#f8fafc',
                                                borderRadius: '8px',
                                                border: '1px solid #e5e7eb',
                                                display: 'flex',
                                                gap: '1rem',
                                                alignItems: 'flex-start'
                                            }}>
                                                <input
                                                    type="checkbox"
                                                    checked={selectedSubIds.includes(item.id)}
                                                    onChange={() => handleSelectToggle(item.id)}
                                                    className="submission-item-checkbox"
                                                    style={{ marginTop: '0.25rem', cursor: 'pointer' }}
                                                />
                                                <div style={{ flex: 1 }}>
                                                    <div className="log-top-header" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                                        <div className="file-identity">
                                                            <span className="version-pill-tag" style={{
                                                                background: '#3b82f6',
                                                                color: 'white',
                                                                padding: '0.15rem 0.6rem',
                                                                borderRadius: '12px',
                                                                fontSize: '0.75rem',
                                                                fontWeight: '600',
                                                                marginRight: '0.5rem'
                                                            }}>
                                                                v{item.version}
                                                            </span>
                                                            <strong className="filename-txt">{item.fileName}</strong>
                                                        </div>
                                                        <span className={`eval-status-indicator ${item.status.toLowerCase().replace(' ', '-')}`} style={{
                                                            padding: '0.15rem 0.6rem',
                                                            borderRadius: '12px',
                                                            fontSize: '0.75rem',
                                                            fontWeight: '600',
                                                            background: item.status === 'Evaluated' ? '#22c55e' : '#f59e0b',
                                                            color: 'white'
                                                        }}>
                                                            {item.status}
                                                        </span>
                                                    </div>

                                                    <p className="metadata-attribution-line" style={{ color: '#6b7280', fontSize: '0.85rem', margin: '0.5rem 0' }}>
                                                        Uploaded at {item.submittedAt} by {item.submittedBy}
                                                    </p>

                                                    {item.score !== undefined && (
                                                        <div className="feedback-result-callout" style={{
                                                            background: '#e0f2fe',
                                                            padding: '0.75rem',
                                                            borderRadius: '6px',
                                                            marginTop: '0.5rem'
                                                        }}>
                                                            <div className="score-summary-line">
                                                                Assigned Score: <strong>{item.score} / 10</strong>
                                                            </div>
                                                            <p className="feedback-text-body" style={{ margin: '0.25rem 0 0', color: '#1e293b' }}>
                                                                "{item.feedback}"
                                                            </p>
                                                        </div>
                                                    )}

                                                    <div className="submission-actions" style={{ marginTop: '0.75rem' }}>
                                                        <button
                                                            onClick={() => handleDownload(item.storedName, item.fileName)}
                                                            className="btn-download-link"
                                                            style={{
                                                                background: 'none',
                                                                border: 'none',
                                                                color: '#3b82f6',
                                                                cursor: 'pointer',
                                                                fontSize: '0.85rem',
                                                                padding: '0.25rem 0.5rem',
                                                                borderRadius: '4px',
                                                                transition: 'background 0.2s'
                                                            }}
                                                            onMouseEnter={(e) => e.currentTarget.style.background = '#eff6ff'}
                                                            onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}
                                                        >
                                                            ⬇️ Download
                                                        </button>
                                                    </div>
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                )}
                                {renderPagination()}
                            </>
                        )}
                    </div>
                </div>
            )}

            {/* --- ✅ FIXED: LECTURER VIEW WITH REAL DATA --- */}
            {role === 'Lecturer' && (
                <div className="submissions-grid-container lecturer-mode" style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1.5rem' }}>
                    {/* Left: Student Deliverables */}
                    <div className="portal-glass-card" style={{ background: 'white', borderRadius: '12px', padding: '1.5rem', boxShadow: '0 1px 3px rgba(0,0,0,0.1)' }}>
                        <h3 className="card-section-title">📥 Incoming Student Deliverables</h3>
                        <div className="selector-control-header">
                            <label htmlFor="project-select" style={{ fontWeight: '500', display: 'block', marginBottom: '0.25rem' }}>
                                Select Project:
                            </label>
                            <select
                                id="project-select"
                                value={selectedProjectId}
                                onChange={handleProjectChange}
                                className="styled-form-dropdown"
                                style={{
                                    width: '100%',
                                    padding: '0.5rem',
                                    borderRadius: '6px',
                                    border: '1px solid #d1d5db',
                                    marginBottom: '1rem'
                                }}
                            >
                                {projects.length === 0 ? (
                                    <option value="">No projects assigned</option>
                                ) : (
                                    projects.map((p) => (
                                        <option key={p.id} value={p.id}>
                                            {p.title} ({p.teamId})
                                        </option>
                                    ))
                                )}
                            </select>
                        </div>

                        {isLoading ? (
                            <p style={{ textAlign: 'center', padding: '1rem', color: '#6b7280' }}>⏳ Loading artifacts...</p>
                        ) : submissions.length === 0 ? (
                            <p style={{ textAlign: 'center', padding: '1rem', color: '#6b7280' }}>📭 No artifacts submitted yet.</p>
                        ) : (
                            <>
                                {renderToolbarAndControls(paginatedSubmissions)}
                                {paginatedSubmissions.length === 0 ? (
                                    <p style={{ textAlign: 'center', padding: '1rem', color: '#6b7280' }}>🔍 No artifacts match search/filters.</p>
                                ) : (
                                    paginatedSubmissions.map((item) => (
                                        <div key={item.id} className="lecturer-artifact-file-row" style={{
                                            padding: '0.75rem',
                                            marginBottom: '0.5rem',
                                            background: '#f8fafc',
                                            borderRadius: '8px',
                                            border: '1px solid #e5e7eb',
                                            display: 'flex',
                                            alignItems: 'center',
                                            gap: '1rem'
                                        }}>
                                            <input
                                                type="checkbox"
                                                checked={selectedSubIds.includes(item.id)}
                                                onChange={() => handleSelectToggle(item.id)}
                                                style={{ cursor: 'pointer' }}
                                            />
                                            <div className="artifact-meta-card" style={{ display: 'flex', alignItems: 'center', gap: '1rem', flex: 1 }}>
                                                <div className="file-icon-block" style={{ fontSize: '1.5rem' }}>📄</div>
                                                <div className="file-text-block" style={{ flex: 1 }}>
                                                    <strong>{item.fileName}</strong>
                                                    <br />
                                                    <span style={{ fontSize: '0.85rem', color: '#6b7280' }}>
                                                        Version {item.version} • Submitted by {item.submittedBy}
                                                    </span>
                                                </div>
                                                <button
                                                    onClick={() => handleDownload(item.storedName, item.fileName)}
                                                    className="btn-download-link"
                                                    style={{
                                                        background: 'none',
                                                        border: 'none',
                                                        color: '#3b82f6',
                                                        cursor: 'pointer',
                                                        padding: '0.25rem 0.5rem'
                                                    }}
                                                >
                                                    ⬇️ Download
                                                </button>
                                            </div>
                                        </div>
                                    ))
                                )}
                                {renderPagination()}
                            </>
                        )}
                    </div>

                    {/* Right: ✅ REAL Evaluation Form */}
                    <div className="portal-glass-card" style={{ background: 'white', borderRadius: '12px', padding: '1.5rem', boxShadow: '0 1px 3px rgba(0,0,0,0.1)' }}>
                        <h3 className="card-section-title">✍️ Perform Evaluation Matrix</h3>

                        {/* Show selected project name */}
                        {selectedProjectId && (
                            <div style={{
                                marginBottom: '1rem',
                                padding: '0.5rem 0.75rem',
                                background: '#f0fdf4',
                                borderRadius: '6px',
                                borderLeft: '3px solid #22c55e'
                            }}>
                                <strong>Evaluating:</strong> {getProjectName(selectedProjectId)}
                            </div>
                        )}

                        <form onSubmit={handleEvaluationSubmit} className="evaluation-input-form">
                            <div className="form-input-group" style={{ marginBottom: '1rem' }}>
                                <label className="input-field-label" style={{ display: 'block', marginBottom: '0.25rem', fontWeight: '500' }}>
                                    Quantitative Milestone Score (0.0 - 10.0) *
                                </label>
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
                                    style={{
                                        width: '100%',
                                        padding: '0.5rem',
                                        borderRadius: '6px',
                                        border: '1px solid #d1d5db',
                                        fontSize: '1rem'
                                    }}
                                    disabled={!selectedProjectId || isSubmittingEval}
                                />
                            </div>

                            <div className="form-input-group" style={{ marginBottom: '1rem' }}>
                                <label className="input-field-label" style={{ display: 'block', marginBottom: '0.25rem', fontWeight: '500' }}>
                                    Qualitative Structural Critique & Feedback *
                                </label>
                                <textarea
                                    rows={5}
                                    required
                                    placeholder="Enter detailed optimization suggestions or rebuttal prerequisites..."
                                    value={evaluationFeedback}
                                    onChange={(e) => setEvaluationFeedback(e.target.value)}
                                    className="styled-form-textarea"
                                    style={{
                                        width: '100%',
                                        padding: '0.5rem',
                                        borderRadius: '6px',
                                        border: '1px solid #d1d5db',
                                        resize: 'vertical',
                                        fontFamily: 'inherit'
                                    }}
                                    disabled={!selectedProjectId || isSubmittingEval}
                                />
                            </div>

                            <button
                                type="submit"
                                className="btn-submit-evaluation-trigger"
                                style={{
                                    width: '100%',
                                    padding: '0.75rem',
                                    background: isSubmittingEval || !selectedProjectId ? '#94a3b8' : '#22c55e',
                                    color: 'white',
                                    border: 'none',
                                    borderRadius: '6px',
                                    cursor: isSubmittingEval || !selectedProjectId ? 'not-allowed' : 'pointer',
                                    fontWeight: '600',
                                    fontSize: '1rem',
                                    transition: 'background 0.2s'
                                }}
                                disabled={isSubmittingEval || !selectedProjectId}
                            >
                                {isSubmittingEval ? '⏳ Submitting...' : '✅ Commit Evaluation Scores'}
                            </button>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
}
