// src/pages/ReviewCalendar.tsx
import type { ReviewSlot } from '../types';
import './ReviewCalendar.css';

export default function ReviewCalendar() {
    // Mock calendar data mapped to scheduling service entities
    const monthlySlots: ReviewSlot[] = [
        { id: 'R1', projectId: 'P101', projectTitle: 'Microservices E-Commerce App', room: 'Alpha 105', time: '2026-06-25 10:00 AM', council: ['Dr. Tran B', 'Prof. Le C'], type: 'Initial Review' },
        { id: 'R2', projectId: 'P102', projectTitle: 'AI Smart Agriculture Tracking', room: 'Beta 202', time: '2026-06-25 02:00 PM', council: ['Dr. Nguyen Van A', 'Prof. Le C'], type: 'Initial Review' },
        { id: 'R3', projectId: 'P103', projectTitle: 'Blockchain Supply Chain Ledger', room: 'Alpha 108', time: '2026-06-26 09:00 AM', council: ['Dr. Tran B', 'Dr. Nguyen Van A'], type: 'Initial Review' },
    ];

    const daysOfWeek = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

    // Generating a dummy month grid for June 2026
    const blankDays = Array(1).fill(null); // June 2026 starts on a Monday
    const monthDays = Array.from({ length: 30 }, (_, i) => i + 1);

    return (
        <div className="calendar-wrapper">
            <div className="calendar-header-section">
                <div>
                    <h2 className="calendar-view-title">Review Schedule Calendar</h2>
                    <p className="calendar-view-subtitle">Displaying active defense sessions for June 2026</p>
                </div>
                <div className="calendar-controls">
                    <button className="btn-nav">&larr; Prev</button>
                    <span className="current-month-display">June 2026</span>
                    <button className="btn-nav">Next &rarr;</button>
                </div>
            </div>

            <div className="calendar-glass-card">
                {/* Day Labels */}
                <div className="days-grid-header">
                    {daysOfWeek.map(day => (
                        <div key={day} className="day-label">{day}</div>
                    ))}
                </div>

                {/* Days Matrix Grid */}
                <div className="days-matrix">
                    {blankDays.map((_, index) => (
                        <div key={`blank-${index}`} className="matrix-cell empty"></div>
                    ))}

                    {monthDays.map(day => {
                        // Check if day matches matching mock items (e.g., June 25 or 26)
                        const dailyEvents = monthlySlots.filter(slot => {
                            const slotDay = parseInt(slot.time.split(' ')[0].split('-')[2]);
                            return slotDay === day;
                        });

                        return (
                            <div key={day} className={`matrix-cell ${dailyEvents.length > 0 ? 'has-events' : ''}`}>
                                <span className="day-number">{day}</span>
                                <div className="cell-events-container">
                                    {dailyEvents.map(event => (
                                        <div key={event.id} className="calendar-event-pill" title={event.projectTitle}>
                                            <span className="event-time-tag">{event.time.split(' ')[1]}</span>
                                            <p className="event-title-truncate">{event.projectTitle}</p>
                                            <div className="event-hover-tooltip">
                                                <strong>{event.type}</strong>
                                                <p>Room: {event.room}</p>
                                                <p>Council: {event.council.join(', ')}</p>
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            </div>
                        );
                    })}
                </div>
            </div>
        </div>
    );
}