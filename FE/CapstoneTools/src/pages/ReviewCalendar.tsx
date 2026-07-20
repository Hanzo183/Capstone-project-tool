// src/pages/ReviewCalendar.tsx
import { useState, useEffect } from 'react';
import { api } from '../api';
import type { ReviewSlot } from '../types';
import './ReviewCalendar.css';

interface ReviewRound {
    id: string;
    name: string;
    startDate: string;
    endDate: string;
}

interface ProjectOption {
    id: string;
    title: string;
}

interface ExtendedReviewSlot extends ReviewSlot {
    status: 'Scheduled' | 'Completed' | 'InProgress' | 'Cancelled';
    roundId: string;
    durationMinutes: number;
}

interface SlotFormState {
    roundId: string;
    projectId: string;
    reviewDate: string;
    room: string;
    durationMinutes: string;
    councilMemberIds: string;
    type: 'Initial Review' | 'Final Defense';
}

const defaultSlotForm: SlotFormState = {
    roundId: '',
    projectId: '',
    reviewDate: '',
    room: '',
    durationMinutes: '60',
    councilMemberIds: '',
    type: 'Initial Review'
};

const canManageSchedule = (role: string | null) =>
    role === 'Admin' || role === 'Lecturer' || role === 'CouncilMember';

const councilMemberIdPattern = /^CM\d{3}$/i;

const toDateInputValue = (value: string) => {
    if (!value || value === 'Not scheduled') return '';
    return value.includes('T') ? value.slice(0, 16) : value.replace(' ', 'T').slice(0, 16);
};

const toDisplayTime = (value: string) => {
    if (!value || value === 'Not scheduled') return 'Not scheduled';
    const parsed = new Date(value);
    if (Number.isNaN(parsed.getTime())) return value;
    return parsed.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
};

const toDisplayDate = (value: string) =>
    new Date(`${value}T00:00:00`).toLocaleDateString(undefined, {
        weekday: 'long',
        year: 'numeric',
        month: 'long',
        day: 'numeric'
    });

export default function ReviewCalendar() {
    const [slots, setSlots] = useState<ExtendedReviewSlot[]>([]);
    const [rounds, setRounds] = useState<ReviewRound[]>([]);
    const [projects, setProjects] = useState<ProjectOption[]>([]);
    const [selectedRoundId, setSelectedRoundId] = useState<string>('all');
    const [selectedDateSlots, setSelectedDateSlots] = useState<ExtendedReviewSlot[]>([]);
    const [selectedDate, setSelectedDate] = useState<string | null>(null);
    const [showSlotForm, setShowSlotForm] = useState(false);
    const [editingSlotId, setEditingSlotId] = useState<string | null>(null);
    const [slotForm, setSlotForm] = useState<SlotFormState>(defaultSlotForm);
    const [formError, setFormError] = useState('');
    const [isSaving, setIsSaving] = useState(false);

    const role = localStorage.getItem('role');
    const userCanManage = canManageSchedule(role);

    const [currentYear, setCurrentYear] = useState(2026);
    const [currentMonth, setCurrentMonth] = useState(5);

    const monthNames = [
        'January', 'February', 'March', 'April', 'May', 'June',
        'July', 'August', 'September', 'October', 'November', 'December'
    ];

    const daysOfWeek = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

    const mapSlot = (s: any, idx: number, projectTitles: Record<string, string>): ExtendedReviewSlot => ({
        id: s.id || `S${idx}`,
        projectId: s.projectId || '',
        projectTitle: s.projectTitle || projectTitles[s.projectId] || s.title || 'Untitled Project',
        room: s.room || 'Room TBD',
        time: s.time || s.reviewDate || 'Not scheduled',
        council: s.council || s.councilMemberIds || s.reviewerIds || [],
        type: (s.type || 'Initial Review') as 'Initial Review' | 'Final Defense',
        status: (s.status || 'Scheduled') as ExtendedReviewSlot['status'],
        roundId: s.roundId || '',
        durationMinutes: s.durationMinutes || 60
    });

    const loadCalendarData = async () => {
        try {
            const [roundsData, slotsData, projectsData] = await Promise.all([
                api.getReviewRounds().catch(() => []),
                api.getScheduleSlots().catch(() => []),
                api.getProjects().catch(() => [])
            ]);

            const realRounds = Array.isArray(roundsData) ? roundsData : [];
            const realProjects = (Array.isArray(projectsData) ? projectsData : []).map((project: any) => ({
                id: project.id,
                title: project.title || project.topicName || project.id
            }));
            const projectTitles = realProjects.reduce<Record<string, string>>((map, project) => {
                map[project.id] = project.title;
                return map;
            }, {});
            const realSlots = (Array.isArray(slotsData) ? slotsData : [])
                .map((slot: any, idx: number) => mapSlot(slot, idx, projectTitles));

            setRounds(realRounds);
            setProjects(realProjects);
            setSlots(realSlots);

            if (selectedDate) {
                setSelectedDateSlots(realSlots.filter(slot => slot.time.startsWith(selectedDate)));
            }
        } catch (err) {
            console.error('Calendar Load Error:', err);
        }
    };

    useEffect(() => {
        loadCalendarData();
    }, []);

    const filteredSlots = slots.filter(slot => selectedRoundId === 'all' || slot.roundId === selectedRoundId);

    const getDaysInMonth = (year: number, month: number) => new Date(year, month + 1, 0).getDate();
    const getFirstDayOfMonth = (year: number, month: number) => new Date(year, month, 1).getDay();

    const daysCount = getDaysInMonth(currentYear, currentMonth);
    const startDay = getFirstDayOfMonth(currentYear, currentMonth);
    const blankDays = Array(startDay).fill(null);
    const daysArray = Array.from({ length: daysCount }, (_, i) => i + 1);

    const formatSlotDateString = (day: number) => {
        const mm = String(currentMonth + 1).padStart(2, '0');
        const dd = String(day).padStart(2, '0');
        return `${currentYear}-${mm}-${dd}`;
    };

    const handlePrevMonth = () => {
        if (currentMonth === 0) {
            setCurrentMonth(11);
            setCurrentYear(prev => prev - 1);
        } else {
            setCurrentMonth(prev => prev - 1);
        }
        setSelectedDate(null);
        setSelectedDateSlots([]);
    };

    const handleNextMonth = () => {
        if (currentMonth === 11) {
            setCurrentMonth(0);
            setCurrentYear(prev => prev + 1);
        } else {
            setCurrentMonth(prev => prev + 1);
        }
        setSelectedDate(null);
        setSelectedDateSlots([]);
    };

    const handleDayClick = (day: number) => {
        const dateStr = formatSlotDateString(day);
        setSelectedDate(dateStr);
        setSelectedDateSlots(filteredSlots.filter(slot => slot.time.startsWith(dateStr)));
    };

    const openCreateForm = () => {
        setEditingSlotId(null);
        setFormError('');
        setSlotForm({
            ...defaultSlotForm,
            roundId: selectedRoundId === 'all' ? (rounds[0]?.id || '') : selectedRoundId,
            projectId: projects[0]?.id || '',
            reviewDate: `${selectedDate || formatSlotDateString(1)}T09:00`
        });
        setShowSlotForm(true);
    };

    const openEditForm = (slot: ExtendedReviewSlot) => {
        setEditingSlotId(slot.id);
        setFormError('');
        setSlotForm({
            roundId: slot.roundId,
            projectId: slot.projectId,
            reviewDate: toDateInputValue(slot.time),
            room: slot.room,
            durationMinutes: String(slot.durationMinutes || 60),
            councilMemberIds: slot.council.join(', '),
            type: slot.type
        });
        setShowSlotForm(true);
    };

    const handleSlotSubmit = async (event: React.FormEvent) => {
        event.preventDefault();
        setFormError('');

        const councilMemberIds = slotForm.councilMemberIds
            .split(',')
            .map(memberId => memberId.trim())
            .filter(Boolean);

        if (!slotForm.roundId || !slotForm.projectId || !slotForm.reviewDate || !slotForm.room.trim()) {
            setFormError('Round, project, review date, and room are required.');
            return;
        }

        if (councilMemberIds.length === 0) {
            setFormError('At least one council member ID is required.');
            return;
        }

        if (councilMemberIds.some(memberId => !councilMemberIdPattern.test(memberId))) {
            setFormError('Council member ID must start with CM followed by 3 numbers, for example CM001.');
            return;
        }

        const durationMinutes = Number(slotForm.durationMinutes);
        if (!Number.isFinite(durationMinutes) || durationMinutes <= 0) {
            setFormError('Duration must be greater than 0 minutes.');
            return;
        }

        const payload = {
            roundId: slotForm.roundId,
            projectId: slotForm.projectId,
            reviewDate: slotForm.reviewDate,
            room: slotForm.room.trim(),
            durationMinutes,
            councilMemberIds: councilMemberIds.map(memberId => memberId.toUpperCase())
        };

        try {
            setIsSaving(true);
            if (editingSlotId) {
                await api.updateScheduleSlot(editingSlotId, payload);
            } else {
                await api.createScheduleSlot(payload);
            }
            setShowSlotForm(false);
            setEditingSlotId(null);
            await loadCalendarData();
        } catch (err: any) {
            setFormError(err.message || 'Failed to save schedule slot.');
        } finally {
            setIsSaving(false);
        }
    };

    const handleDeleteSlot = async (slot: ExtendedReviewSlot) => {
        if (!window.confirm(`Delete schedule slot for "${slot.projectTitle}"?`)) {
            return;
        }

        try {
            await api.deleteScheduleSlot(slot.id);
            if (editingSlotId === slot.id) {
                setShowSlotForm(false);
                setEditingSlotId(null);
            }
            await loadCalendarData();
        } catch (err: any) {
            alert(err.message || 'Failed to delete schedule slot.');
        }
    };

    return (
        <div className="calendar-wrapper">
            <div className="calendar-header-section">
                <div>
                    <h2 className="calendar-view-title">Review Calendar</h2>
                    <p className="calendar-view-subtitle">Filter by rounds and click dates to view schedule slots</p>
                </div>

                <div className="calendar-actions-filter-bar">
                    {userCanManage && (
                        <button className="btn-manage-slot primary" onClick={openCreateForm}>
                            Create Schedule
                        </button>
                    )}

                    <div className="filter-select-wrapper">
                        <label htmlFor="round-filter-dropdown" className="filter-label">Round Filter:</label>
                        <select
                            id="round-filter-dropdown"
                            value={selectedRoundId}
                            onChange={(e) => {
                                setSelectedRoundId(e.target.value);
                                setSelectedDate(null);
                                setSelectedDateSlots([]);
                            }}
                            className="round-select-input"
                        >
                            <option value="all">All Review Rounds</option>
                            {rounds.map(r => (
                                <option key={r.id} value={r.id}>{r.name}</option>
                            ))}
                        </select>
                    </div>

                    <div className="calendar-controls">
                        <button className="btn-nav" onClick={handlePrevMonth}>&larr; Prev</button>
                        <span className="current-month-display">{monthNames[currentMonth]} {currentYear}</span>
                        <button className="btn-nav" onClick={handleNextMonth}>Next &rarr;</button>
                    </div>
                </div>
            </div>

            {userCanManage && showSlotForm && (
                <form className="schedule-editor-panel" onSubmit={handleSlotSubmit}>
                    <div className="schedule-editor-header">
                        <h3>{editingSlotId ? 'Update Schedule' : 'Create Schedule'}</h3>
                        <button type="button" className="btn-manage-slot ghost" onClick={() => setShowSlotForm(false)}>
                            Close
                        </button>
                    </div>

                    {formError && <p className="schedule-form-error">{formError}</p>}

                    <div className="schedule-form-grid">
                        <label>
                            Round
                            <select value={slotForm.roundId} onChange={(e) => setSlotForm({ ...slotForm, roundId: e.target.value })} required>
                                <option value="">Select round</option>
                                {rounds.map(round => (
                                    <option key={round.id} value={round.id}>{round.name}</option>
                                ))}
                            </select>
                        </label>

                        <label>
                            Project
                            <select value={slotForm.projectId} onChange={(e) => setSlotForm({ ...slotForm, projectId: e.target.value })} required>
                                <option value="">Select project</option>
                                {projects.map(project => (
                                    <option key={project.id} value={project.id}>{project.title}</option>
                                ))}
                            </select>
                        </label>

                        <label>
                            Review Date
                            <input
                                type="datetime-local"
                                value={slotForm.reviewDate}
                                onChange={(e) => setSlotForm({ ...slotForm, reviewDate: e.target.value })}
                                required
                            />
                        </label>

                        <label>
                            Room
                            <input
                                type="text"
                                value={slotForm.room}
                                onChange={(e) => setSlotForm({ ...slotForm, room: e.target.value })}
                                placeholder="Alpha 105"
                                required
                            />
                        </label>

                        <label>
                            Duration
                            <input
                                type="number"
                                min="1"
                                value={slotForm.durationMinutes}
                                onChange={(e) => setSlotForm({ ...slotForm, durationMinutes: e.target.value })}
                                required
                            />
                        </label>

                        <label>
                            Type
                            <select value={slotForm.type} onChange={(e) => setSlotForm({ ...slotForm, type: e.target.value as SlotFormState['type'] })}>
                                <option value="Initial Review">Initial Review</option>
                                <option value="Final Defense">Final Defense</option>
                            </select>
                        </label>

                        <label className="schedule-form-wide">
                            Council Member IDs
                            <input
                                type="text"
                                value={slotForm.councilMemberIds}
                                onChange={(e) => setSlotForm({ ...slotForm, councilMemberIds: e.target.value })}
                                placeholder="CM001, CM002"
                                required
                            />
                        </label>
                    </div>

                    <div className="schedule-form-actions">
                        <button type="submit" className="btn-manage-slot primary" disabled={isSaving}>
                            {isSaving ? 'Saving...' : editingSlotId ? 'Update Schedule' : 'Create Schedule'}
                        </button>
                        {editingSlotId && (
                            <button type="button" className="btn-manage-slot danger" onClick={() => {
                                const slot = slots.find(item => item.id === editingSlotId);
                                if (slot) handleDeleteSlot(slot);
                            }}>
                                Delete Schedule
                            </button>
                        )}
                    </div>
                </form>
            )}

            <div className="calendar-content-split">
                <div className="calendar-main-grid">
                    <div className="calendar-legend">
                        <div className="legend-item">
                            <span className="legend-dot status-scheduled"></span>
                            <span>Scheduled</span>
                        </div>
                        <div className="legend-item">
                            <span className="legend-dot status-inprogress"></span>
                            <span>In Progress</span>
                        </div>
                        <div className="legend-item">
                            <span className="legend-dot status-completed"></span>
                            <span>Completed</span>
                        </div>
                        <div className="legend-item">
                            <span className="legend-dot status-cancelled"></span>
                            <span>Cancelled</span>
                        </div>
                    </div>

                    <div className="calendar-glass-card">
                        <div className="days-grid-header">
                            {daysOfWeek.map(day => (
                                <div key={day} className="day-label">{day}</div>
                            ))}
                        </div>

                        <div className="days-matrix">
                            {blankDays.map((_, index) => (
                                <div key={`blank-${index}`} className="matrix-cell empty"></div>
                            ))}

                            {daysArray.map(day => {
                                const dateStr = formatSlotDateString(day);
                                const dailyEvents = filteredSlots.filter(slot => slot.time.startsWith(dateStr));
                                const isSelected = selectedDate === dateStr;

                                return (
                                    <div
                                        key={day}
                                        onClick={() => handleDayClick(day)}
                                        className={`matrix-cell ${dailyEvents.length > 0 ? 'has-events' : ''} ${isSelected ? 'selected-day' : ''}`}
                                    >
                                        <span className="day-number">{day}</span>
                                        <div className="cell-events-container">
                                            {dailyEvents.map(event => (
                                                <div
                                                    key={event.id}
                                                    className={`calendar-event-pill status-${event.status.toLowerCase()}`}
                                                    title={event.projectTitle}
                                                >
                                                    <p className="event-title-truncate">{event.projectTitle}</p>
                                                </div>
                                            ))}
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                    </div>
                </div>

                <div className="calendar-details-panel">
                    <div className="details-panel-header">
                        <h3>Slots for {selectedDate ? toDisplayDate(selectedDate) : 'Select a date'}</h3>
                    </div>

                    <div className="details-panel-content">
                        {!selectedDate ? (
                            <div className="no-selection-prompt">
                                <span className="prompt-icon">Calendar</span>
                                <p>Click on any highlighted date in the calendar to view its assigned defense review slots.</p>
                            </div>
                        ) : selectedDateSlots.length === 0 ? (
                            <p className="no-slots-message">No defense slots scheduled for this date.</p>
                        ) : (
                            <div className="details-slots-list">
                                {selectedDateSlots.map(slot => (
                                    <div key={slot.id} className={`slot-details-card border-${slot.status.toLowerCase()}`}>
                                        <div className="slot-card-header">
                                            <span className={`status-pill status-${slot.status.toLowerCase()}`}>{slot.status}</span>
                                            <span className="slot-type-badge">{slot.type}</span>
                                        </div>
                                        <h4 className="slot-project-title">{slot.projectTitle}</h4>
                                        <div className="slot-meta-items">
                                            <p>Room: <strong>{slot.room}</strong></p>
                                            <p>Time: <strong>{toDisplayTime(slot.time)}</strong></p>
                                            <p>Duration: <strong>{slot.durationMinutes} minutes</strong></p>
                                            <p>Reviewers: {slot.council.length > 0 ? slot.council.join(', ') : 'Unassigned'}</p>
                                        </div>
                                        {userCanManage && (
                                            <div className="slot-management-actions">
                                                <button type="button" className="btn-manage-slot" onClick={() => openEditForm(slot)}>
                                                    Update
                                                </button>
                                                <button type="button" className="btn-manage-slot danger" onClick={() => handleDeleteSlot(slot)}>
                                                    Delete
                                                </button>
                                            </div>
                                        )}
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>
                </div>
            </div>
        </div>
    );
}
