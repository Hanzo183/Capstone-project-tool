// src/pages/Login.tsx
import { useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { api } from '../api';
import './Login.css';

// Decode JWT token to extract role and user info
const decodeJwtToken = (token: string) => {
    try {
        const payload = JSON.parse(atob(token.split('.')[1]));
        return {
            role: payload.role || payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || null,
            userId: payload.nameid || payload.sub || null,
            fullName: payload.fullName || payload.name || null,
            email: payload.email || null
        };
    } catch (e) {
        console.error('Failed to decode token:', e);
        return null;
    }
};

export default function Login() {
    const navigate = useNavigate();
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [error, setError] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const [showForgotPassword, setShowForgotPassword] = useState(false);
    const [resetEmail, setResetEmail] = useState('');
    const [resetMessage, setResetMessage] = useState('');

    const handleLoginSubmit = async (e: FormEvent<HTMLFormElement>) => {
        e.preventDefault();
        setError('');
        setIsLoading(true);

        try {
            const data = await api.login({ email, password });
            const token = data.token;

            if (!token) {
                throw new Error('No token received from server.');
            }

            // Decode token to get user info
            const decoded = decodeJwtToken(token);
            if (!decoded) {
                throw new Error('Invalid token structure.');
            }

            // Store user info in localStorage
            localStorage.setItem('token', token);
            localStorage.setItem('role', decoded.role || '');
            localStorage.setItem('userId', decoded.userId || '');
            localStorage.setItem('fullName', decoded.fullName || '');
            localStorage.setItem('email', decoded.email || '');

            // Redirect based on role
            const role = decoded.role?.toLowerCase() || '';
            if (role === 'admin') {
                navigate('/admin');
            } else if (role === 'lecturer') {
                navigate('/lecturer');
            } else if (role === 'councilmember') {
                navigate('/council');
            } else {
                navigate('/student');
            }

        } catch (err: any) {
            setError(err.message || 'Failed to connect to Identity Service.');
        } finally {
            setIsLoading(false);
        }
    };

    const handleForgotPassword = async (e: FormEvent<HTMLFormElement>) => {
        e.preventDefault();
        setResetMessage('');
        setError('');

        try {
            await api.forgotPassword(resetEmail);
            setResetMessage('Password reset link sent to your email!');
            setTimeout(() => {
                setShowForgotPassword(false);
                setResetEmail('');
                setResetMessage('');
            }, 3000);
        } catch (err: any) {
            setError(err.message || 'Failed to send reset email.');
        }
    };

    return (
        <div className="login-page-wrapper">
            <div className="login-glass-card">
                <h2 className="login-title">Capstone Review Tool</h2>
                <p className="login-subtitle">Sign in to access your tracking workspace</p>

                {showForgotPassword ? (
                    // --- FORGOT PASSWORD FORM ---
                    <form onSubmit={handleForgotPassword} className="login-form">
                        <div className="input-group">
                            <label htmlFor="resetEmail">Enter your FPT Email</label>
                            <input
                                type="email"
                                id="resetEmail"
                                value={resetEmail}
                                onChange={(e) => setResetEmail(e.target.value)}
                                placeholder="username@fpt.edu.vn"
                                required
                            />
                        </div>
                        {resetMessage && <div className="login-success-msg">{resetMessage}</div>}
                        {error && <div className="login-error-msg">{error}</div>}
                        <button type="submit" className="login-submit-btn">
                            Send Reset Link
                        </button>
                        <button
                            type="button"
                            className="login-back-btn"
                            onClick={() => {
                                setShowForgotPassword(false);
                                setError('');
                                setResetMessage('');
                            }}
                        >
                            ← Back to Login
                        </button>
                    </form>
                ) : (
                    // --- LOGIN FORM ---
                    <form onSubmit={handleLoginSubmit} className="login-form">
                        {error && <div className="login-error-msg">{error}</div>}

                        <div className="input-group">
                            <label htmlFor="email">FPT Email Address</label>
                            <input
                                type="email"
                                id="email"
                                value={email}
                                onChange={(e) => setEmail(e.target.value)}
                                placeholder="username@fpt.edu.vn"
                                required
                            />
                        </div>

                        <div className="input-group">
                            <label htmlFor="password">Password</label>
                            <input
                                type="password"
                                id="password"
                                value={password}
                                onChange={(e) => setPassword(e.target.value)}
                                placeholder="••••••••"
                                required
                            />
                        </div>

                        <div className="login-options">
                            <button
                                type="button"
                                className="forgot-password-link"
                                onClick={() => setShowForgotPassword(true)}
                            >
                                
                            </button>
                        </div>

                        <button type="submit" className="login-submit-btn" disabled={isLoading}>
                            {isLoading ? 'Authenticating...' : 'Sign In to System'}
                        </button>

                        {/* ✅ ADDED: Register Link */}
                        <div className="login-footer">
                            <p>
                                Don't have an account? <Link to="/register" className="register-link">Create Account</Link>
                            </p>
                        </div>
                    </form>
                )}
            </div>
        </div>
    );
}