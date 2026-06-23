// src/pages/Login.tsx
import React, { useState, FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import './Login.css';

export default function Login() {
    const navigate = useNavigate();
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [role, setRole] = useState<'Student' | 'Admin' | 'Lecturer'>('Student');
    const [error, setError] = useState('');

    const handleLoginSubmit = (e: FormEvent<HTMLFormElement>) => {
        e.preventDefault();

        if (!email || !password) {
            setError('Please provide valid login credentials.');
            return;
        }

        // 1. Persist the mock tokens into your browser's local memory
        localStorage.setItem('token', 'mock-jwt-auth-token-xyz123');
        localStorage.setItem('role', role);

        // 2. Break out of the login loop and navigate back to the root guard node
        navigate('/', { replace: true });
    };

    return (
        <div className="login-page-wrapper">
            <div className="login-glass-card">
                <h2 className="login-title">Capstone Review Tool</h2>
                <p className="login-subtitle">Sign in to access your tracking workspace</p>

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

                    {/* Development Selection Node - Overrides the auth assignment criteria */}
                    <div className="input-group">
                        <label>Testing Profile Node</label>
                        <div className="role-selector-pills">
                            {(['Student', 'Admin', 'Lecturer'] as const).map((targetRole) => (
                                <button
                                    key={targetRole}
                                    type="button"
                                    className={`role-pill ${role === targetRole ? 'active' : ''}`}
                                    onClick={() => setRole(targetRole)}
                                >
                                    {targetRole}
                                </button>
                            ))}
                        </div>
                    </div>

                    <button type="submit" className="login-submit-btn">
                        Sign In to System
                    </button>
                </form>
            </div>
        </div>
    );
}