// src/pages/Register.tsx
import { useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { api } from '../api';
import './Register.css';

interface RegisterFormData {
    email: string;
    password: string;
    confirmPassword: string;
    fullName: string;
    studentId: string;
    role: string;
}

const studentIdPattern = /^[A-Za-z]{2}\d{6}$/;
const emailPattern = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

export default function Register() {
    const navigate = useNavigate();
    const [formData, setFormData] = useState<RegisterFormData>({
        email: '',
        password: '',
        confirmPassword: '',
        fullName: '',
        studentId: '',
        role: 'Student' // Default role
    });
    const [error, setError] = useState('');
    const [success, setSuccess] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const [passwordStrength, setPasswordStrength] = useState(0);

    // Password strength checker
    const checkPasswordStrength = (password: string) => {
        let strength = 0;
        if (password.length >= 8) strength++;
        if (password.match(/[a-z]/)) strength++;
        if (password.match(/[A-Z]/)) strength++;
        if (password.match(/[0-9]/)) strength++;
        if (password.match(/[^a-zA-Z0-9]/)) strength++;
        setPasswordStrength(strength);
    };

    const handleChange = (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => {
        const { name, value } = e.target;
        setFormData(prev => ({ ...prev, [name]: value }));

        // Check password strength when password changes
        if (name === 'password') {
            checkPasswordStrength(value);
        }
    };

    const handleRegisterSubmit = async (e: FormEvent<HTMLFormElement>) => {
        e.preventDefault();
        setError('');
        setSuccess('');
        setIsLoading(true);

        // Validation
        if (!formData.fullName.trim()) {
            setError('Full name is required.');
            setIsLoading(false);
            return;
        }

        if (!emailPattern.test(formData.email.trim())) {
            setError('Please enter a valid email address.');
            setIsLoading(false);
            return;
        }

        if (formData.role === 'Student' && !formData.studentId.trim()) {
            setError('Student ID is required for student accounts.');
            setIsLoading(false);
            return;
        }

        if (formData.studentId.trim() && !studentIdPattern.test(formData.studentId.trim())) {
            setError('Student ID must start with 2 letters followed by 6 numbers, for example SE192706.');
            setIsLoading(false);
            return;
        }

        if (formData.password !== formData.confirmPassword) {
            setError('Passwords do not match!');
            setIsLoading(false);
            return;
        }

        if (formData.password.length < 8) {
            setError('Password must be at least 8 characters long!');
            setIsLoading(false);
            return;
        }

        try {
            // Call register API
            await api.register({
                email: formData.email.trim(),
                password: formData.password,
                fullName: formData.fullName.trim(),
                studentId: formData.studentId.trim() ? formData.studentId.trim().toUpperCase() : undefined,
                role: formData.role
            });

            setSuccess('✅ Registration successful! Redirecting to login...');

            // Clear form
            setFormData({
                email: '',
                password: '',
                confirmPassword: '',
                fullName: '',
                studentId: '',
                role: 'Student'
            });
            setPasswordStrength(0);

            // Redirect to login after 2 seconds
            setTimeout(() => {
                navigate('/login');
            }, 2000);

        } catch (err: any) {
            setError(err.message || 'Registration failed. Please try again.');
        } finally {
            setIsLoading(false);
        }
    };

    // Get password strength label
    const getPasswordStrengthLabel = () => {
        if (passwordStrength === 0) return '';
        if (passwordStrength <= 2) return 'Weak';
        if (passwordStrength <= 3) return 'Fair';
        if (passwordStrength <= 4) return 'Good';
        return 'Strong';
    };

    // Get password strength color
    const getPasswordStrengthColor = () => {
        if (passwordStrength === 0) return '#e5e7eb';
        if (passwordStrength <= 2) return '#ef4444';
        if (passwordStrength <= 3) return '#f59e0b';
        if (passwordStrength <= 4) return '#3b82f6';
        return '#22c55e';
    };

    return (
        <div className="register-page-wrapper">
            <div className="register-glass-card">
                <div className="register-header">
                    <h2 className="register-title">Create Account</h2>
                    <p className="register-subtitle">Register for the Capstone Review Tool</p>
                </div>

                <form onSubmit={handleRegisterSubmit} className="register-form">
                    {error && <div className="register-error-msg">❌ {error}</div>}
                    {success && <div className="register-success-msg">✅ {success}</div>}

                    {/* Full Name */}
                    <div className="input-group">
                        <label htmlFor="fullName">Full Name *</label>
                        <input
                            type="text"
                            id="fullName"
                            name="fullName"
                            value={formData.fullName}
                            onChange={handleChange}
                            placeholder="Nguyen Van A"
                            required
                        />
                    </div>

                    {/* Email */}
                    <div className="input-group">
                        <label htmlFor="email">FPT Email Address *</label>
                        <input
                            type="email"
                            id="email"
                            name="email"
                            value={formData.email}
                            onChange={handleChange}
                            placeholder="username@fpt.edu.vn"
                            required
                        />
                    </div>

                    {/* Student ID */}
                    <div className="input-group">
                        <label htmlFor="studentId">Student ID</label>
                        <input
                            type="text"
                            id="studentId"
                            name="studentId"
                            value={formData.studentId}
                            onChange={handleChange}
                            placeholder="SE192706"
                            pattern="[A-Za-z]{2}[0-9]{6}"
                            title="Student ID must start with 2 letters followed by 6 numbers, for example SE192706."
                            required={formData.role === 'Student'}
                        />
                    </div>

                    {/* Password */}
                    <div className="input-group">
                        <label htmlFor="password">Password *</label>
                        <input
                            type="password"
                            id="password"
                            name="password"
                            value={formData.password}
                            onChange={handleChange}
                            placeholder="••••••••"
                            required
                            minLength={8}
                        />
                        {formData.password && (
                            <div className="password-strength-container">
                                <div className="password-strength-bar">
                                    <div
                                        className="password-strength-fill"
                                        style={{
                                            width: `${(passwordStrength / 5) * 100}%`,
                                            backgroundColor: getPasswordStrengthColor(),
                                            transition: 'width 0.3s ease'
                                        }}
                                    />
                                </div>
                                <span
                                    className="password-strength-label"
                                    style={{ color: getPasswordStrengthColor() }}
                                >
                                    {getPasswordStrengthLabel()}
                                </span>
                            </div>
                        )}
                        <small className="password-hint">
                            Minimum 8 characters
                        </small>
                    </div>

                    {/* Confirm Password */}
                    <div className="input-group">
                        <label htmlFor="confirmPassword">Confirm Password *</label>
                        <input
                            type="password"
                            id="confirmPassword"
                            name="confirmPassword"
                            value={formData.confirmPassword}
                            onChange={handleChange}
                            placeholder="••••••••"
                            required
                        />
                        {formData.confirmPassword && formData.password !== formData.confirmPassword && (
                            <small className="password-error">Passwords do not match!</small>
                        )}
                        {formData.confirmPassword && formData.password === formData.confirmPassword && (
                            <small className="password-match">✅ Passwords match!</small>
                        )}
                    </div>

                    <button type="submit" className="register-submit-btn" disabled={isLoading}>
                        {isLoading ? 'Creating Account...' : 'Create Account'}
                    </button>

                    <div className="register-footer">
                        <p>
                            Already have an account? <Link to="/login" className="register-login-link">Sign In</Link>
                        </p>
                    </div>
                </form>
            </div>
        </div>
    );
}
