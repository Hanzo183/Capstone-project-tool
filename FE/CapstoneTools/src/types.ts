// src/types.ts
export type Role = 'Student' | 'Lecturer' | 'CouncilMember' | 'Admin';
export type ProjectStatus = 'Draft' | 'Submitted' | 'In Review' | 'Needs Revision' | 'Approved';

export type Project = {
    id: string;
    title: string;
    team: string;
    lecturer: string;
    status: ProjectStatus;
    round: string;
    score?: number;
    updatedAt: string;
};

export type Submission = {
    id: string;
    projectId: string;
    fileName: string;
    version: number;
    submittedAt: string;
    submittedBy: string;
    status: 'Pending' | 'Evaluated';
};

export type ReviewSlot = {
    id: string;
    projectId: string;
    projectTitle: string;
    room: string;
    time: string;
    council: string[];
    type: 'Initial Review' | 'Final Defense';
};

export type Evaluation = {
    id: string;
    evaluator: string;
    score: number;
    feedback: string;
    submittedAt: string;
    canRebuttal: boolean;
};

export type ScheduleEvent = {
    id: string;
    title: string;
    date: string;
    status: 'Completed' | 'Current' | 'Upcoming';
};