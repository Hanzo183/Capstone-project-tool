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

interface ExtendedReviewSlot extends ReviewSlot {
    status: 'Scheduled' | 'Completed' | 'InProgress' | 'Cancelled';
    roundId: string;
}

export default function ReviewCalendar() {
    const [slots, setSlots] = useState<ExtendedReviewSlot[]>([]);
    const [rounds, setRounds] = useState<ReviewRound[]>([]);
    const [selectedRoundId, setSelectedRoundId] = useState<string>('all');
    const [selectedDateSlots, setSelectedDateSlots] = useState<ExtendedReviewSlot[]>([]);
    const [selectedDate, setSelectedDate] = useState<string | null>(null);

    const [currentYear, setCurrentYear] = useState(2026);
    const [currentMonth, setCurrentMonth] = useState(5); // June (0-indexed represents June as month 5)

    const monthNames = [
        'January', 'February', 'March', 'April', 'May', 'June',
        'July', 'August', 'September', 'October', 'November', 'December'
    ];

    const daysOfWeek = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

    const loadCalendarData = async () => {
        try {
            // Get Review Rounds
            let roundsData: any[] = [];
            try {
                roundsData = await api.getReviewRounds();
            } catch (err) {
                console.warn("Failed to fetch rounds, using fallback:", err);
            }

            if (!roundsData || roundsData.length === 0) {
                roundsData = [
                    { id: 'RND101', name: 'Spring 2026 - Iteration 1', startDate: '2026-06-01', endDate: '2026-06-30' },
                    { id: 'RND102', name: 'Spring 2026 - Final Defense', startDate: '2026-07-01', endDate: '2026-07-15' }
                ];
            }
            setRounds(roundsData);

            // Get Review Slots
            let slotsData: any[] = [];
            try {
                slotsData = await api.getScheduleSlots();
            } catch (err) {
                console.warn("Failed to fetch slots, using fallback:", err);
            }

            if (!slotsData || slotsData.length === 0) {
                slotsData = [
                    { id: 'R1', projectId: 'P101', projectTitle: 'Microservices E-Commerce App', room: 'Alpha 105', time: '2026-06-25 10:00 AM', council: ['Dr. Nguyen Van A', 'Prof. Le C'], type: 'Initial Review', status: 'Completed', roundId: 'RND101' },
                    { id: 'R2', projectId: 'P102', projectTitle: 'AI Smart Agriculture Tracking', room: 'Beta 202', time: '2026-06-25 02:00 PM', council: ['Dr. Nguyen Van A', 'Prof. Le C'], type: 'Initial Review', status: 'InProgress', roundId: 'RND101' },
                    { id: 'R3', projectId: 'P103', projectTitle: 'Blockchain Supply Chain Ledger', room: 'Alpha 108', time: '2026-06-26 09:00 AM', council: ['Dr. Tran B', 'Dr. Nguyen Van A'], type: 'Initial Review', status: 'Scheduled', roundId: 'RND101' },
                    { id: 'R4', projectId: 'P104', projectTitle: 'IoT Weather Forecasting Station', room: 'Alpha 105', time: '2026-07-05 10:00 AM', council: ['Dr. Tran B', 'Prof. Le C'], type: 'Final Defense', status: 'Scheduled', roundId: 'RND102' }
                ];
            } else {
                // Ensure all keys are mapped
                slotsData = slotsData.map((s, idx) => ({
                    id: s.id || `S${idx}`,
                    projectId: s.projectId || 'P_UNKNOWN',
                    projectTitle: s.projectTitle || s.title || 'Untitled Project',
                    room: s.room || 'Room Alpha',
                    time: s.time || s.reviewDate || '2026-06-25 10:00 AM',
                    council: s.council || s.reviewerIds || ['Unassigned Council'],
                    type: s.type || 'Initial Review',
                    status: s.status || 'Scheduled',
                    roundId: s.roundId || 'RND101'
                }));
            }

            setSlots(slotsData);
        } catch (err) {
            console.error("Calendar Load Error:", err);
        }
    };

    useEffect(() => {
        loadCalendarData();
    }, []);

    // Filter slots based on selected round
    const filteredSlots = slots.filter(slot => {
        if (selectedRoundId === 'all') return true;
        return slot.roundId === selectedRoundId;
    });

    // Helper to get number of days in month
    const getDaysInMonth = (year: number, month: number) => {
        return new Date(year, month + 1, 0).getDate();
    };

    // Helper to get start day of the month (0 = Sunday, 1 = Monday, etc.)
    const getFirstDayOfMonth = (year: number, month: number) => {
        return new Date(year, month, 1).getDay();
    };

    const daysCount = getDaysInMonth(currentYear, currentMonth);
    const startDay = getFirstDayOfMonth(currentYear, currentMonth);

    const blankDays = Array(startDay).fill(null);
    const daysArray = Array.from({ length: daysCount }, (_, i) => i + 1);

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

    const formatSlotDateString = (day: number) => {
        const mm = String(currentMonth + 1).padStart(2, '0');
        const dd = String(day).padStart(2, '0');
        return `${currentYear}-${mm}-${dd}`;
    };

    const handleDayClick = (day: number) => {
        const dateStr = formatSlotDateString(day);
        const daySlots = filteredSlots.filter(slot => {
            return slot.time.startsWith(dateStr);
        });
        setSelectedDate(dateStr);
        setSelectedDateSlots(daySlots);
    };

    return (
        <div className="calendar-wrapper">
            <div className="calendar-header-section">
                <div>
                    <h2 className="calendar-view-title">Review Calendar</h2>
                    <p className="calendar-view-subtitle">Filter by rounds and click dates to view schedule slots</p>
                </div>
                
                <div className="calendar-actions-filter-bar">
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

            <div className="calendar-content-split">
                {/* Calendar Grid View */}
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
                                const dailyEvents = filteredSlots.filter(slot => {
                                    return slot.time.startsWith(dateStr);
                                });

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

                {/* Day Slots Details Side Panel */}
                <div className="calendar-details-panel">
                    <div className="details-panel-header">
                        <h3>Slots for {selectedDate ? new Date(selectedDate).toLocaleDateString(undefined, { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' }) : 'Select a date'}</h3>
                    </div>
                    
                    <div className="details-panel-content">
                        {!selectedDate ? (
                            <div className="no-selection-prompt">
                                <span className="prompt-icon">📅</span>
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
                                            <p>📍 Room: <strong>{slot.room}</strong></p>
                                            <p>⏰ Time: <strong>{slot.time.split(' ')[1] + ' ' + (slot.time.split(' ')[2] || '')}</strong></p>
                                            <p>👥 Reviewers: {slot.council.join(', ')}</p>
                                        </div>
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