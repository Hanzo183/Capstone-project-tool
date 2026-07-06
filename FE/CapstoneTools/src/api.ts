// src/api.ts
const API_BASE_URL = 'http://localhost:5000';

export const api = {
    // ============================================
    // 🔐 IDENTITY SERVICE - AUTHENTICATION
    // ============================================

    // Login
    login: async (credentials: { email: string, password: string }) => {
        const response = await fetch(`${API_BASE_URL}/auth/login`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(credentials),
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Invalid email or password');
        }

        const data = await response.json();

        if (data && data.accessToken) {
            localStorage.setItem('token', data.accessToken);
            localStorage.setItem('refreshToken', data.refreshToken || '');

            try {
                const payload = JSON.parse(atob(data.accessToken.split('.')[1]));
                localStorage.setItem('userId', payload.nameid || payload.sub || '');
                localStorage.setItem('role', payload.role || '');
                localStorage.setItem('fullName', payload.fullName || payload.name || '');
                localStorage.setItem('email', payload.email || '');
            } catch (e) {
                console.warn('Could not decode token:', e);
            }
        }

        return {
            token: data.accessToken,
            refreshToken: data.refreshToken,
            user: data.user
        };
    },

    // Logout
    logout: async () => {
        const token = localStorage.getItem('token');
        try {
            await fetch(`${API_BASE_URL}/auth/logout`, {
                method: 'POST',
                headers: {
                    'Authorization': `Bearer ${token}`,
                    'Content-Type': 'application/json'
                }
            });
        } catch (err) {
            console.warn('Logout API call failed:', err);
        } finally {
            localStorage.removeItem('token');
            localStorage.removeItem('refreshToken');
            localStorage.removeItem('userId');
            localStorage.removeItem('role');
            localStorage.removeItem('fullName');
            localStorage.removeItem('email');
        }
    },

    // Register new user
    register: async (userData: {
        email: string;
        password: string;
        fullName: string;
        studentId?: string;
        role?: string;
    }) => {
        const response = await fetch(`${API_BASE_URL}/auth/register`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(userData)
        });
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Registration failed');
        }
        return response.json();
    },

    // Refresh token
    refreshToken: async () => {
        const refreshToken = localStorage.getItem('refreshToken');
        const response = await fetch(`${API_BASE_URL}/auth/refresh`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ refreshToken })
        });
        if (!response.ok) {
            throw new Error('Failed to refresh token');
        }
        const data = await response.json();
        if (data && data.accessToken) {
            localStorage.setItem('token', data.accessToken);
        }
        return data;
    },

    // Forgot password - request reset
    forgotPassword: async (email: string) => {
        const response = await fetch(`${API_BASE_URL}/auth/forgot-password`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ email })
        });
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Failed to send reset email');
        }
        return response.json();
    },

    // Reset password with token
    resetPassword: async (resetData: {
        token: string;
        newPassword: string;
    }) => {
        const response = await fetch(`${API_BASE_URL}/auth/reset-password`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(resetData)
        });
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Failed to reset password');
        }
        return response.json();
    },

    // ============================================
    // 👥 IDENTITY SERVICE - USER MANAGEMENT
    // ============================================

    // Get all users (Admin only)
    getUsers: async () => {
        const token = localStorage.getItem('token');
        if (!token) {
            throw new Error('No authentication token found');
        }

        try {
            const response = await fetch(`${API_BASE_URL}/users`, {
                headers: {
                    'Authorization': `Bearer ${token}`,
                    'Content-Type': 'application/json'
                }
            });

            if (!response.ok) {
                if (response.status === 401) {
                    throw new Error('Unauthorized - Please login again');
                }
                if (response.status === 403) {
                    throw new Error('Forbidden - Admin access required');
                }
                throw new Error(`Failed to fetch users: ${response.status}`);
            }

            return response.json();
        } catch (err) {
            console.error('getUsers error:', err);
            throw err;
        }
    },

    // Get user by ID
    getUser: async (userId: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/users/${userId}`, {
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            }
        });
        if (!response.ok) {
            throw new Error('Failed to fetch user');
        }
        return response.json();
    },

    // Update user role (Admin only)
    updateUserRole: async (userId: string, role: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/users/${userId}/role`, {
            method: 'PUT',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ role })
        });
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Failed to update user role');
        }
        return response.json();
    },

    // Update user status (Admin only)
    updateUserStatus: async (userId: string, isActive: boolean) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/users/${userId}/status`, {
            method: 'PUT',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ isActive })
        });
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Failed to update user status');
        }
        return response.json();
    },

    // ============================================
    // 📋 PROJECT SERVICE ENDPOINTS
    // ============================================

    // Get all projects
    getProjects: async () => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/projects`, {
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            }
        });
        if (!response.ok) {
            throw new Error('Failed to fetch projects');
        }
        return response.json();
    },

    // Get single project
    getProject: async (projectId: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/projects/${projectId}`, {
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            }
        });
        if (!response.ok) {
            throw new Error('Failed to fetch project');
        }
        return response.json();
    },

    // Create new project (Lecturer/Admin)
    createProject: async (projectData: {
        title: string;
        description: string;
        teamId: string;
        teamLeaderId?: string;
        lecturerId: string;
        roundId?: string;
        status?: string;
    }) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/projects`, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(projectData)
        });
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Failed to create project');
        }
        return response.json();
    },

    // Submit project for review
    submitProject: async (projectId: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/projects/${projectId}/submit`, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            }
        });
        if (!response.ok) {
            throw new Error('Failed to submit project');
        }
        return response.json();
    },

    // Update project status (Lecturer/Admin)
    updateProjectStatus: async (projectId: string, status: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/projects/${projectId}/status`, {
            method: 'PATCH',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ status })
        });
        if (!response.ok) {
            throw new Error('Failed to update status');
        }
        return response.json();
    },

    // Get submission history
    getSubmissions: async (projectId: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/projects/${projectId}/history`, {
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            }
        });
        if (!response.ok) {
            throw new Error('Failed to fetch submissions');
        }
        return response.json();
    },

    // Upload submission
    uploadSubmission: async (projectId: string, formData: FormData) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/projects/${projectId}/submissions/upload`, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${token}`
            },
            body: formData
        });
        if (!response.ok) {
            throw new Error('Failed to upload submission');
        }
        return response.json();
    },

    // Download file
    downloadFile: async (projectId: string, storedName: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/projects/${projectId}/submissions/files/${storedName}`, {
            headers: {
                'Authorization': `Bearer ${token}`
            }
        });
        if (!response.ok) {
            throw new Error('Failed to download file');
        }
        return response.blob();
    },

    // ============================================
    // 📅 SCHEDULING SERVICE ENDPOINTS
    // ============================================

    // Get all review rounds
    getReviewRounds: async () => {
        const token = localStorage.getItem('token');
        try {
            const response = await fetch(`${API_BASE_URL}/rounds`, {
                headers: {
                    'Authorization': `Bearer ${token}`,
                    'Content-Type': 'application/json'
                }
            });
            if (!response.ok) {
                if (response.status === 404) {
                    console.warn('Rounds endpoint not found, returning empty array');
                    return [];
                }
                throw new Error('Failed to fetch review rounds');
            }
            return response.json();
        } catch (err) {
            console.error('getReviewRounds error:', err);
            return [];
        }
    },

    // Get single review round
    getReviewRound: async (roundId: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/rounds/${roundId}`, {
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            }
        });
        if (!response.ok) {
            throw new Error('Failed to fetch review round');
        }
        return response.json();
    },

    // Create review round (Admin only)
    createReviewRound: async (roundData: {
        name: string;
        startDate: string;
        endDate: string;
        createdBy: string;
    }) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/rounds`, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(roundData)
        });
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Failed to create review round');
        }
        return response.json();
    },

    // Get slots for a round
    getSlotsByRound: async (roundId: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/rounds/${roundId}/slots`, {
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            }
        });
        if (!response.ok) {
            throw new Error('Failed to fetch slots');
        }
        return response.json();
    },

    // Get all slots
    getScheduleSlots: async () => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/slots`, {
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            }
        });
        if (!response.ok) {
            throw new Error('Failed to fetch schedule slots');
        }
        return response.json();
    },

    // Create schedule slot (Admin only)
    createScheduleSlot: async (slotData: {
        roundId: string;
        projectId: string;
        reviewDate: string;
        room: string;
        durationMinutes?: number;
        reviewerIds?: string[];
    }) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/slots`, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(slotData)
        });
        if (!response.ok) {
            throw new Error('Failed to create schedule slot');
        }
        return response.json();
    },

    // ============================================
    // ⭐ EVALUATION SERVICE ENDPOINTS
    // ============================================

    // Get evaluations for a project
    getEvaluations: async (projectId: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/evaluations/project/${projectId}`, {
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            }
        });
        if (!response.ok) {
            throw new Error('Failed to fetch evaluations');
        }
        return response.json();
    },

    // Create evaluation (Lecturer/Council)
    createEvaluation: async (evalData: {
        projectId: string;
        roundId: string;
        evaluatorId: string;
        score: number;
        feedback: string;
    }) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/evaluations`, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(evalData)
        });
        if (!response.ok) {
            throw new Error('Failed to create evaluation');
        }
        return response.json();
    },

    // Get rebuttals for an evaluation
    getRebuttals: async (evaluationId: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/rebuttals/evaluation/${evaluationId}`, {
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            }
        });
        if (!response.ok) {
            throw new Error('Failed to fetch rebuttals');
        }
        return response.json();
    },

    // Create rebuttal (Student)
    createRebuttal: async (rebuttalData: {
        evaluationId: string;
        studentId: string;
        content: string;
    }) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/rebuttals`, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(rebuttalData)
        });
        if (!response.ok) {
            throw new Error('Failed to create rebuttal');
        }
        return response.json();
    },

    // Respond to rebuttal (Council)
    respondToRebuttal: async (rebuttalId: string, responseText: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/rebuttals/${rebuttalId}/respond`, {
            method: 'PUT',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ response: responseText })
        });
        if (!response.ok) {
            throw new Error('Failed to respond to rebuttal');
        }
        return response.json();
    },

    // Update rebuttal status
    updateRebuttalStatus: async (rebuttalId: string, status: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/rebuttals/${rebuttalId}/status`, {
            method: 'PUT',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ status })
        });
        if (!response.ok) {
            throw new Error('Failed to update rebuttal status');
        }
        return response.json();
    },

    // Get reports for a round
    getReports: async (roundId: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/reports/${roundId}`, {
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            }
        });
        if (!response.ok) {
            throw new Error('Failed to fetch reports');
        }
        return response.json();
    },

    // Download report as PDF
    downloadReportPdf: async (roundId: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/reports/${roundId}/pdf`, {
            headers: {
                'Authorization': `Bearer ${token}`
            }
        });
        if (!response.ok) {
            throw new Error('Failed to download report');
        }
        return response.blob();
    },

    // ============================================
    // 🔔 NOTIFICATION SERVICE ENDPOINTS
    // ============================================

    // Get notifications for current user
    getNotifications: async () => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/notifications`, {
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            }
        });
        if (!response.ok) {
            throw new Error('Failed to fetch notifications');
        }
        return response.json();
    },

    // Mark notification as read
    markNotificationRead: async (notificationId: string) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/notifications/${notificationId}/read`, {
            method: 'PATCH',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            }
        });
        if (!response.ok) {
            throw new Error('Failed to mark notification as read');
        }
        return response.json();
    },

    // Mark all notifications as read
    markAllNotificationsRead: async () => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/notifications/read-all`, {
            method: 'PATCH',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            }
        });
        if (!response.ok) {
            throw new Error('Failed to mark all notifications as read');
        }
        return response.json();
    },

    // Get notification preferences
    getNotificationPreferences: async () => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/notifications/preferences`, {
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            }
        });
        if (!response.ok) {
            throw new Error('Failed to fetch notification preferences');
        }
        return response.json();
    },

    // Update notification preferences
    updateNotificationPreferences: async (preferences: {
        emailEnabled: boolean;
        inAppEnabled: boolean;
    }) => {
        const token = localStorage.getItem('token');
        const response = await fetch(`${API_BASE_URL}/notifications/preferences`, {
            method: 'PUT',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(preferences)
        });
        if (!response.ok) {
            throw new Error('Failed to update notification preferences');
        }
        return response.json();
    }
};