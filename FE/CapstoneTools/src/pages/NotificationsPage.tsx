// src/pages/NotificationsPage.tsx
import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api';
import './NotificationsPage.css';

interface NotificationItem {
    id: string;
    title: string;
    body: string;
    createdAt: string;
    isRead: boolean;
}

export default function NotificationsPage() {
    const navigate = useNavigate();
    const [notifications, setNotifications] = useState<NotificationItem[]>([]);
    const [preferences, setPreferences] = useState({ emailEnabled: true, inAppEnabled: true });
    const [isLoading, setIsLoading] = useState(true);
    const [successMsg, setSuccessMsg] = useState('');

    const [sortBy, setSortBy] = useState<'newest' | 'oldest'>('newest');
    const [currentPage, setCurrentPage] = useState(1);
    const [pageSize, setPageSize] = useState(5);
    const [totalNotifications, setTotalNotifications] = useState(0);

    const loadNotificationsData = async (silent = false) => {
        if (!silent) setIsLoading(true);
        try {
            // Get preferences
            try {
                const prefs = await api.getNotificationPreferences();
                if (prefs) setPreferences(prefs);
            } catch (err) {
                console.warn("Preferences endpoint failure, using fallback:", err);
            }

            // Get notifications
            let notifsData: any[] = [];
            let total = 0;
            try {
                const res = await api.getNotifications(currentPage, pageSize);
                if (res && Array.isArray(res.items)) {
                    notifsData = res.items;
                    total = res.totalCount || res.items.length;
                } else if (Array.isArray(res)) {
                    notifsData = res;
                    total = res.length;
                }
            } catch (err) {
                console.warn("Notifications list failure, using fallback:", err);
            }

            if (!notifsData || notifsData.length === 0) {
                const stored = localStorage.getItem('local_notifications');
                if (stored) {
                    notifsData = JSON.parse(stored);
                    total = notifsData.length;
                } else {
                    notifsData = [
                        { id: 'N1', title: 'Review Slot Assigned', body: 'You have been assigned to evaluate the "Microservices E-Commerce App" project on June 25 at 10:00 AM.', createdAt: new Date(Date.now() - 3600000 * 2).toISOString(), isRead: false },
                        { id: 'N2', title: 'New Submission Uploaded', body: 'Team G2 has uploaded a new PDF artifact for "AI Smart Agriculture Tracking".', createdAt: new Date(Date.now() - 3600000 * 5).toISOString(), isRead: false },
                        { id: 'N3', title: 'Rebuttal Received', body: 'A new rebuttal comment has been submitted by student Tran Van Bao.', createdAt: new Date(Date.now() - 3600000 * 24).toISOString(), isRead: true }
                    ];
                    localStorage.setItem('local_notifications', JSON.stringify(notifsData));
                    total = notifsData.length;
                }
            }

            const mapped: NotificationItem[] = notifsData.map(n => ({
                id: n.id,
                title: n.title || 'Notification Alert',
                body: n.body || n.message || '',
                createdAt: n.createdAt || n.createdTime || new Date().toISOString(),
                isRead: !!n.isRead || !!n.read
            }));

            // Sort locally
            const sorted = mapped.sort((a, b) => {
                const timeA = new Date(a.createdAt).getTime();
                const timeB = new Date(b.createdAt).getTime();
                return sortBy === 'newest' ? timeB - timeA : timeA - timeB;
            });

            setNotifications(sorted);
            setTotalNotifications(total);
            updateBadge(sorted);
        } catch (err) {
            console.error("Failed to load notifications:", err);
        } finally {
            if (!silent) setIsLoading(false);
        }
    };

    const updateBadge = (items: NotificationItem[]) => {
        const unreadCount = items.filter(n => !n.isRead).length;
        localStorage.setItem('unreadNotificationsCount', unreadCount.toString());
        // Dispatch custom event to notify Sidebar (Layout)
        window.dispatchEvent(new Event('notificationsUpdated'));
    };

    useEffect(() => {
        loadNotificationsData();

        // Auto-refresh interval (every 10 seconds)
        const interval = setInterval(() => {
            loadNotificationsData(true);
        }, 10000);

        return () => clearInterval(interval);
    }, [currentPage, pageSize, sortBy]);

    const handleMarkAsRead = async (id: string) => {
        try {
            await api.markNotificationRead(id);
        } catch (err) {
            console.warn("API mark read failure, doing local update:", err);
        }

        const updated = notifications.map(n => n.id === id ? { ...n, isRead: true } : n);
        setNotifications(updated);
        localStorage.setItem('local_notifications', JSON.stringify(updated));
        updateBadge(updated);
    };

    const handleMarkAllAsRead = async () => {
        try {
            await api.markAllNotificationsRead();
        } catch (err) {
            console.warn("API mark all read failure, doing local update:", err);
        }

        const updated = notifications.map(n => ({ ...n, isRead: true }));
        setNotifications(updated);
        localStorage.setItem('local_notifications', JSON.stringify(updated));
        updateBadge(updated);
        setSuccessMsg('All notifications marked as read.');
        setTimeout(() => setSuccessMsg(''), 3000);
    };

    const handleDeleteNotification = (id: string) => {
        const updated = notifications.filter(n => n.id !== id);
        setNotifications(updated);
        localStorage.setItem('local_notifications', JSON.stringify(updated));
        updateBadge(updated);
    };

    const handleNotificationClick = async (item: NotificationItem) => {
        if (!item.isRead) {
            await handleMarkAsRead(item.id);
        }

        const title = item.title.toLowerCase();
        const body = item.body.toLowerCase();

        if (title.includes('slot') || title.includes('schedule') || body.includes('schedule') || body.includes('review slot') || body.includes('assigned to evaluate') || body.includes('review is scheduled')) {
            navigate('/review-calendar');
        } else if (title.includes('evaluation') || title.includes('feedback') || title.includes('rebuttal') || body.includes('rebuttal') || body.includes('score') || body.includes('feedback') || body.includes('rejected') || body.includes('evaluated')) {
            navigate('/evaluations');
        } else if (title.includes('submission') || title.includes('artifact') || body.includes('submission') || body.includes('artifact') || body.includes('uploaded')) {
            navigate('/submissions');
        } else {
            navigate('/dashboard');
        }
    };

    const handleTogglePreference = async (key: 'emailEnabled' | 'inAppEnabled') => {
        const nextPrefs = { ...preferences, [key]: !preferences[key] };
        setPreferences(nextPrefs);

        try {
            await api.updateNotificationPreferences(nextPrefs);
        } catch (err) {
            console.warn("API update preferences failure:", err);
        }
    };

    if (isLoading) {
        return (
            <div className="loading-container">
                <div className="spinner"></div>
                <p>Loading Notifications...</p>
            </div>
        );
    }

    return (
        <div className="notifications-page-container">
            <header className="page-header-block">
                <div>
                    <h2 className="page-main-title">Notification Center</h2>
                    <p className="page-subtitle">Read system alerts, reviews updates, and manage your delivery preferences</p>
                </div>
            </header>

            {successMsg && <div className="toast-success-alert">✅ {successMsg}</div>}

            <div className="notifications-split-layout">
                {/* Left: Alerts List */}
                <div className="notifications-feed-section">
                    <div className="feed-header-actions">
                        <h3>Alert Feed</h3>
                        {notifications.some(n => !n.isRead) && (
                            <button className="btn-action-text" onClick={handleMarkAllAsRead}>
                                Mark All as Read
                            </button>
                        )}
                    </div>

                    {notifications.length === 0 ? (
                        <div className="empty-feed-card">
                            <span className="empty-bell-icon">🔔</span>
                            <p>All quiet here! You have no notifications.</p>
                        </div>
                    ) : (
                        <div className="notifications-list-grid">
                            {notifications.map(item => (
                                <div key={item.id} className={`notification-item-card ${item.isRead ? 'read-item' : 'unread-item'}`}>
                                    <div className="card-indicator-dot"></div>
                                    <div className="card-content-block">
                                        <div 
                                            className="card-clickable-area" 
                                            onClick={() => handleNotificationClick(item)}
                                            style={{ cursor: 'pointer' }}
                                        >
                                            <div className="card-meta">
                                                <h4>{item.title}</h4>
                                                <span className="date-tag">{new Date(item.createdAt).toLocaleString()}</span>
                                            </div>
                                            <p className="body-text">{item.body}</p>
                                        </div>
                                        
                                        <div className="card-action-triggers">
                                            {!item.isRead && (
                                                <button 
                                                    className="btn-card-action" 
                                                    onClick={() => handleMarkAsRead(item.id)}
                                                >
                                                    Mark as Read
                                                </button>
                                            )}
                                            <button 
                                                className="btn-card-action btn-delete" 
                                                onClick={() => handleDeleteNotification(item.id)}
                                            >
                                                Delete
                                            </button>
                                        </div>
                                    </div>
                                </div>
                            ))}
                        </div>
                    )}
                </div>

                {/* Right: Preference Toggles */}
                <div className="notifications-preferences-panel">
                    <div className="preference-card-wrapper">
                        <h3>Delivery Preferences</h3>
                        <p className="card-desc">Choose how you want to be notified of defense slots, reviews, and updates.</p>
                        
                        <div className="preference-options-list">
                            <div className="preference-row">
                                <div className="pref-info">
                                    <strong>In-App Alerts</strong>
                                    <p>Receive live alerts inside the system navigation bar.</p>
                                </div>
                                <label className="switch-toggle-input">
                                    <input 
                                        type="checkbox" 
                                        checked={preferences.inAppEnabled} 
                                        onChange={() => handleTogglePreference('inAppEnabled')}
                                    />
                                    <span className="slider-switch-round"></span>
                                </label>
                            </div>

                            <div className="preference-row">
                                <div className="pref-info">
                                    <strong>Email Summaries</strong>
                                    <p>Send slot changes and reports directly to your inbox.</p>
                                </div>
                                <label className="switch-toggle-input">
                                    <input 
                                        type="checkbox" 
                                        checked={preferences.emailEnabled} 
                                        onChange={() => handleTogglePreference('emailEnabled')}
                                    />
                                    <span className="slider-switch-round"></span>
                                </label>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
}
