// src/pages/LecturerDashboard.tsx
import './LecturerDashboard.css';

interface MentoredGroup {
    id: string;
    topicName: string;
    studentLeader: string;
    memberCount: number;
    submissionStatus: 'Submitted' | 'Pending' | 'Overdue';
    lastUpdated: string;
}

interface UpcomingCouncil {
    id: string;
    timeSlot: string;
    room: string;
    projectTitle: string;
    role: 'Chairman' | 'Secretary' | 'Member';
}

export default function LecturerDashboard() {
    // Mock data representing mentored thesis groups
    const mentoredGroups: MentoredGroup[] = [
        { id: 'G01', topicName: 'Microservices E-Commerce App', studentLeader: 'Le Van An', memberCount: 4, submissionStatus: 'Submitted', lastUpdated: 'Today, 02:30 PM' },
        { id: 'G02', topicName: 'AI Smart Agriculture Tracking', studentLeader: 'Tran Thi Binh', memberCount: 5, submissionStatus: 'Pending', lastUpdated: '2 days ago' },
        { id: 'G03', topicName: 'Blockchain Supply Chain Ledger', studentLeader: 'Pham Minh Hoang', memberCount: 4, submissionStatus: 'Overdue', lastUpdated: 'Passed Deadline' },
    ];

    // Mock data for upcoming evaluation panels
    const upcomingCouncils: UpcomingCouncil[] = [
        { id: 'C01', timeSlot: 'June 25, 10:00 AM', room: 'Alpha 105', projectTitle: 'IoT Home Automation Hub', role: 'Chairman' },
        { id: 'C02', timeSlot: 'June 25, 02:00 PM', room: 'Beta 202', projectTitle: 'Mobile Health Diagnostics', role: 'Member' },
    ];

    return (
        <div className="lecturer-dashboard-container">
            {/* Dashboard Top Header Section */}
            <div className="lecturer-header-block">
                <div>
                    <h2 className="lecturer-title">Lecturer Workspace Portal</h2>
                    <p className="lecturer-subtitle">Track your mentored thesis cohorts and defense council assignments.</p>
                </div>
                <div className="quick-stats-row">
                    <div className="stat-pill">
                        <span className="stat-dot green"></span>
                        <strong>3</strong> Mentored Groups
                    </div>
                    <div className="stat-pill">
                        <span className="stat-dot indigo"></span>
                        <strong>2</strong> Active Councils
                    </div>
                </div>
            </div>

            {/* Main Content Layout Grid */}
            <div className="lecturer-grid-layout">

                {/* Left Column: Mentored Groups Monitoring Panel */}
                <div className="lecturer-glass-card main-panel">
                    <h3 className="panel-section-title">Your Mentored Capstone Groups</h3>
                    <div className="groups-stack">
                        {mentoredGroups.map((group) => (
                            <div key={group.id} className="group-item-card">
                                <div className="group-main-details">
                                    <span className="group-id-badge">{group.id}</span>
                                    <div>
                                        <h4>{group.topicName}</h4>
                                        <p>Leader: {group.studentLeader} • {group.memberCount} Members</p>
                                    </div>
                                </div>
                                <div className="group-status-actions">
                                    <div className="status-timestamp-block">
                                        <span className={`status-pill-badge ${group.submissionStatus.toLowerCase()}`}>
                                            {group.submissionStatus}
                                        </span>
                                        <span className="timestamp-txt">{group.lastUpdated}</span>
                                    </div>
                                    <button className="btn-interact-action">Review Artifacts</button>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>

                {/* Right Column: Upcoming Evaluation Council Schedules */}
                <div className="lecturer-glass-card sidebar-panel">
                    <h3 className="panel-section-title">Upcoming Council Panels</h3>
                    <div className="councils-vertical-timeline">
                        {upcomingCouncils.map((council) => (
                            <div key={council.id} className="council-timeline-node">
                                <div className="node-time-marker">
                                    <span className="clock-emoji">⏰</span>
                                    <div>
                                        <span className="time-string">{council.timeSlot}</span>
                                        <span className="room-string">Room: {council.room}</span>
                                    </div>
                                </div>
                                <div className="node-body-details">
                                    <h5>{council.projectTitle}</h5>
                                    <div className="role-assignment-tag">
                                        Assigned Role: <span>{council.role}</span>
                                    </div>
                                    <button className="btn-grade-trigger">Open Evaluation Sheet</button>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>

            </div>
        </div>
    );
}