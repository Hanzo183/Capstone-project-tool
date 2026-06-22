import { useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import './App.css'

type Role = 'Student' | 'Lecturer' | 'CouncilMember' | 'Admin'
type ProjectStatus = 'Draft' | 'Submitted' | 'In Review' | 'Needs Revision' | 'Approved'

type Project = {
  id: string
  title: string
  team: string
  lecturer: string
  status: ProjectStatus
  round: string
  score?: number
  updatedAt: string
}

type Submission = {
  id: string
  projectId: string
  fileName: string
  version: number
  submittedAt: string
  submittedBy: string
}

type ReviewSlot = {
  id: string
  projectId: string
  projectTitle: string
  room: string
  time: string
  council: string
}

type NotificationItem = {
  id: string
  title: string
  body: string
  type: string
  isRead: boolean
  createdAt: string
}

type User = {
  id: string
  name: string
  email: string
  role: Role
  active: boolean
}

const initialProjects: Project[] = [
  {
    id: 'p-1001',
    title: 'AI Review Scheduler',
    team: 'Team 6',
    lecturer: 'Tran Tuan Minh',
    status: 'In Review',
    round: 'Spring 2025 Round 1',
    score: 8.5,
    updatedAt: 'Today, 09:40',
  },
  {
    id: 'p-1002',
    title: 'Submission Quality Tracker',
    team: 'Team 2',
    lecturer: 'Tran Tuan Minh',
    status: 'Submitted',
    round: 'Spring 2025 Round 1',
    updatedAt: 'Yesterday, 16:15',
  },
  {
    id: 'p-1003',
    title: 'Council Scoring Portal',
    team: 'Team 4',
    lecturer: 'Luong Pham Binh Minh',
    status: 'Needs Revision',
    round: 'Spring 2025 Round 1',
    score: 7.4,
    updatedAt: 'Jun 18, 14:30',
  },
]

const initialSubmissions: Submission[] = [
  {
    id: 's-1',
    projectId: 'p-1001',
    fileName: 'proposal-v1.pdf',
    version: 1,
    submittedAt: 'Jun 15, 10:12',
    submittedBy: 'Nguyen Chinh Nhan',
  },
  {
    id: 's-2',
    projectId: 'p-1001',
    fileName: 'architecture-v2.pdf',
    version: 2,
    submittedAt: 'Jun 21, 08:30',
    submittedBy: 'Nguyen Chinh Nhan',
  },
]

const reviewSlots: ReviewSlot[] = [
  {
    id: 'r-1',
    projectId: 'p-1001',
    projectTitle: 'AI Review Scheduler',
    room: 'B3-201',
    time: 'Jun 25, 09:00',
    council: 'Council Reviewer, Tran Tuan Minh',
  },
  {
    id: 'r-2',
    projectId: 'p-1002',
    projectTitle: 'Submission Quality Tracker',
    room: 'B3-202',
    time: 'Jun 25, 10:00',
    council: 'Council Reviewer, Luong Pham Binh Minh',
  },
]

const initialNotifications: NotificationItem[] = [
  {
    id: 'n-1',
    title: 'Review slot assigned',
    body: 'AI Review Scheduler is scheduled in B3-201.',
    type: 'schedule.created',
    isRead: false,
    createdAt: 'Today, 08:15',
  },
  {
    id: 'n-2',
    title: 'Submission ready',
    body: 'Team 6 uploaded architecture-v2.pdf.',
    type: 'project.submitted',
    isRead: false,
    createdAt: 'Yesterday, 16:20',
  },
  {
    id: 'n-3',
    title: 'Feedback released',
    body: 'Council feedback is available for Council Scoring Portal.',
    type: 'evaluation.completed',
    isRead: true,
    createdAt: 'Jun 18, 15:10',
  },
]

const initialUsers: User[] = [
  {
    id: 'SE192737',
    name: 'Luong Pham Binh Minh',
    email: 'minh.backend@fpt.edu.vn',
    role: 'Admin',
    active: true,
  },
  {
    id: 'SE192706',
    name: 'Nguyen Chinh Nhan',
    email: 'nhan.frontend@fpt.edu.vn',
    role: 'Student',
    active: true,
  },
  {
    id: 'SE192879',
    name: 'Tran Tuan Minh',
    email: 'minh.jobs@fpt.edu.vn',
    role: 'Lecturer',
    active: true,
  },
  {
    id: 'CM001',
    name: 'Council Reviewer',
    email: 'council@fpt.edu.vn',
    role: 'CouncilMember',
    active: true,
  },
]

const tabs = ['Overview', 'Projects', 'Schedule', 'Evaluations', 'Admin'] as const
type Tab = (typeof tabs)[number]

function App() {
  const [activeTab, setActiveTab] = useState<Tab>('Overview')
  const [projects, setProjects] = useState(initialProjects)
  const [submissions, setSubmissions] = useState(initialSubmissions)
  const [notifications, setNotifications] = useState(initialNotifications)
  const [users, setUsers] = useState(initialUsers)
  const [selectedProjectId, setSelectedProjectId] = useState(projects[0].id)
  const [fileName, setFileName] = useState('')
  const [rebuttal, setRebuttal] = useState('')

  const selectedProject = projects.find((project) => project.id === selectedProjectId) ?? projects[0]
  const selectedSubmissions = submissions.filter((submission) => submission.projectId === selectedProject.id)

  const metrics = useMemo(() => {
    const pendingReviews = projects.filter((project) => project.status === 'Submitted' || project.status === 'In Review').length
    const overdueSubmissions = projects.filter((project) => project.status === 'Draft' || project.status === 'Needs Revision').length
    const completionRate = Math.round((projects.filter((project) => project.status === 'Approved').length / projects.length) * 100)

    return {
      totalProjects: projects.length,
      pendingReviews,
      overdueSubmissions,
      completionRate,
    }
  }, [projects])

  function handleSubmission(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const trimmedFileName = fileName.trim()
    if (!trimmedFileName) {
      return
    }

    const nextVersion =
      selectedSubmissions.reduce((highest, submission) => Math.max(highest, submission.version), 0) + 1

    setSubmissions((current) => [
      {
        id: `s-${Date.now()}`,
        projectId: selectedProject.id,
        fileName: trimmedFileName,
        version: nextVersion,
        submittedAt: 'Just now',
        submittedBy: 'Nguyen Chinh Nhan',
      },
      ...current,
    ])

    setProjects((current) =>
      current.map((project) =>
        project.id === selectedProject.id
          ? { ...project, status: 'Submitted', updatedAt: 'Just now' }
          : project,
      ),
    )

    setNotifications((current) => [
      {
        id: `n-${Date.now()}`,
        title: 'Submission uploaded',
        body: `${trimmedFileName} was added to ${selectedProject.title}.`,
        type: 'project.submitted',
        isRead: false,
        createdAt: 'Just now',
      },
      ...current,
    ])

    setFileName('')
  }

  function markNotificationRead(id: string) {
    setNotifications((current) =>
      current.map((notification) =>
        notification.id === id ? { ...notification, isRead: true } : notification,
      ),
    )
  }

  function submitRebuttal(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const content = rebuttal.trim()
    if (!content) {
      return
    }

    setNotifications((current) => [
      {
        id: `n-${Date.now()}`,
        title: 'Rebuttal submitted',
        body: `${selectedProject.team} submitted a rebuttal for ${selectedProject.title}.`,
        type: 'rebuttal.submitted',
        isRead: false,
        createdAt: 'Just now',
      },
      ...current,
    ])
    setRebuttal('')
  }

  function toggleUserStatus(id: string) {
    setUsers((current) =>
      current.map((user) => (user.id === id ? { ...user, active: !user.active } : user)),
    )
  }

  return (
    <main className="app-shell">
      <aside className="sidebar" aria-label="Main navigation">
        <div className="brand">
          <div className="brand-mark" aria-hidden="true">CR</div>
          <div>
            <strong>Capstone Review</strong>
            <span>Scheduling and Tracking</span>
          </div>
        </div>

        <nav className="tabs" aria-label="Workspace">
          {tabs.map((tab) => (
            <button
              className={activeTab === tab ? 'tab active' : 'tab'}
              key={tab}
              onClick={() => setActiveTab(tab)}
              type="button"
            >
              {tab}
            </button>
          ))}
        </nav>

        <div className="session-panel">
          <span className="eyebrow">Signed in</span>
          <strong>Nguyen Chinh Nhan</strong>
          <span>Student</span>
        </div>
      </aside>

      <section className="workspace">
        <header className="topbar">
          <div>
            <span className="eyebrow">Spring 2025</span>
            <h1>{activeTab}</h1>
          </div>
          <div className="topbar-actions">
            <button type="button" onClick={() => setActiveTab('Schedule')}>Open Calendar</button>
            <button className="primary" type="button" onClick={() => setActiveTab('Projects')}>New Submission</button>
          </div>
        </header>

        {activeTab === 'Overview' && (
          <section className="view-grid">
            <div className="metrics">
              <Metric label="Total Projects" value={metrics.totalProjects} accent="teal" />
              <Metric label="Pending Reviews" value={metrics.pendingReviews} accent="coral" />
              <Metric label="Revision Queue" value={metrics.overdueSubmissions} accent="gold" />
              <Metric label="Completion" value={`${metrics.completionRate}%`} accent="green" />
            </div>

            <section className="panel wide">
              <div className="panel-heading">
                <div>
                  <span className="eyebrow">Project Status</span>
                  <h2>Review Pipeline</h2>
                </div>
              </div>
              <div className="status-board">
                {['Draft', 'Submitted', 'In Review', 'Needs Revision', 'Approved'].map((status) => (
                  <div className="status-lane" key={status}>
                    <span>{status}</span>
                    <strong>{projects.filter((project) => project.status === status).length}</strong>
                  </div>
                ))}
              </div>
            </section>

            <section className="panel">
              <div className="panel-heading">
                <div>
                  <span className="eyebrow">Notifications</span>
                  <h2>Latest Events</h2>
                </div>
              </div>
              <NotificationList notifications={notifications.slice(0, 4)} onRead={markNotificationRead} />
            </section>

            <section className="panel">
              <div className="panel-heading">
                <div>
                  <span className="eyebrow">Next Reviews</span>
                  <h2>Schedule</h2>
                </div>
              </div>
              <div className="stack">
                {reviewSlots.map((slot) => (
                  <article className="compact-row" key={slot.id}>
                    <div>
                      <strong>{slot.projectTitle}</strong>
                      <span>{slot.room} | {slot.council}</span>
                    </div>
                    <time>{slot.time}</time>
                  </article>
                ))}
              </div>
            </section>
          </section>
        )}

        {activeTab === 'Projects' && (
          <section className="split-view">
            <section className="panel">
              <div className="panel-heading">
                <div>
                  <span className="eyebrow">Projects</span>
                  <h2>Assigned Work</h2>
                </div>
              </div>
              <div className="project-list">
                {projects.map((project) => (
                  <button
                    className={selectedProject.id === project.id ? 'project-row selected' : 'project-row'}
                    key={project.id}
                    onClick={() => setSelectedProjectId(project.id)}
                    type="button"
                  >
                    <span>
                      <strong>{project.title}</strong>
                      <small>{project.team} | {project.lecturer}</small>
                    </span>
                    <StatusBadge status={project.status} />
                  </button>
                ))}
              </div>
            </section>

            <section className="panel detail-panel">
              <div className="panel-heading">
                <div>
                  <span className="eyebrow">{selectedProject.round}</span>
                  <h2>{selectedProject.title}</h2>
                </div>
                <StatusBadge status={selectedProject.status} />
              </div>

              <div className="detail-grid">
                <LabelValue label="Team" value={selectedProject.team} />
                <LabelValue label="Lecturer" value={selectedProject.lecturer} />
                <LabelValue label="Last Update" value={selectedProject.updatedAt} />
                <LabelValue label="Score" value={selectedProject.score ? selectedProject.score.toFixed(1) : 'Pending'} />
              </div>

              <form className="inline-form" onSubmit={handleSubmission}>
                <label htmlFor="fileName">Submission File</label>
                <input
                  id="fileName"
                  onChange={(event) => setFileName(event.target.value)}
                  placeholder="artifact-v3.pdf"
                  value={fileName}
                />
                <button className="primary" type="submit">Submit</button>
              </form>

              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Version</th>
                      <th>File</th>
                      <th>Submitted</th>
                      <th>Owner</th>
                    </tr>
                  </thead>
                  <tbody>
                    {selectedSubmissions.map((submission) => (
                      <tr key={submission.id}>
                        <td>v{submission.version}</td>
                        <td>{submission.fileName}</td>
                        <td>{submission.submittedAt}</td>
                        <td>{submission.submittedBy}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          </section>
        )}

        {activeTab === 'Schedule' && (
          <section className="panel">
            <div className="panel-heading">
              <div>
                <span className="eyebrow">Review Round</span>
                <h2>Spring 2025 Round 1</h2>
              </div>
              <button type="button">Assign Slot</button>
            </div>
            <div className="schedule-grid">
              {reviewSlots.map((slot) => (
                <article className="schedule-card" key={slot.id}>
                  <time>{slot.time}</time>
                  <h3>{slot.projectTitle}</h3>
                  <p>{slot.room}</p>
                  <span>{slot.council}</span>
                </article>
              ))}
            </div>
          </section>
        )}

        {activeTab === 'Evaluations' && (
          <section className="split-view">
            <section className="panel">
              <div className="panel-heading">
                <div>
                  <span className="eyebrow">Council Review</span>
                  <h2>Scorecard</h2>
                </div>
              </div>
              <div className="scorecard">
                <strong>{selectedProject.score ? selectedProject.score.toFixed(1) : 'Pending'}</strong>
                <span>Architecture is clear; add stronger testing evidence.</span>
              </div>
              <div className="rubric">
                <LabelValue label="Architecture" value="8.8" />
                <LabelValue label="Implementation" value="8.3" />
                <LabelValue label="Testing" value="7.9" />
                <LabelValue label="Demo Readiness" value="8.6" />
              </div>
            </section>

            <section className="panel">
              <div className="panel-heading">
                <div>
                  <span className="eyebrow">Student Response</span>
                  <h2>Rebuttal</h2>
                </div>
              </div>
              <form className="rebuttal-form" onSubmit={submitRebuttal}>
                <textarea
                  onChange={(event) => setRebuttal(event.target.value)}
                  placeholder="Clarify implementation progress or request another review."
                  value={rebuttal}
                />
                <button className="primary" type="submit">Submit Rebuttal</button>
              </form>
            </section>
          </section>
        )}

        {activeTab === 'Admin' && (
          <section className="view-grid">
            <section className="panel wide">
              <div className="panel-heading">
                <div>
                  <span className="eyebrow">Users</span>
                  <h2>Role Management</h2>
                </div>
                <button type="button">Create User</button>
              </div>
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>ID</th>
                      <th>Name</th>
                      <th>Email</th>
                      <th>Role</th>
                      <th>Status</th>
                      <th>Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {users.map((user) => (
                      <tr key={user.id}>
                        <td>{user.id}</td>
                        <td>{user.name}</td>
                        <td>{user.email}</td>
                        <td>{user.role}</td>
                        <td>{user.active ? 'Active' : 'Inactive'}</td>
                        <td>
                          <button type="button" onClick={() => toggleUserStatus(user.id)}>
                            {user.active ? 'Deactivate' : 'Activate'}
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>

            <section className="panel">
              <div className="panel-heading">
                <div>
                  <span className="eyebrow">Reports</span>
                  <h2>Round Output</h2>
                </div>
              </div>
              <div className="stack">
                <button className="primary" type="button">Generate PDF</button>
                <button type="button">Email Reports</button>
                <button type="button">Download CSV</button>
              </div>
            </section>
          </section>
        )}
      </section>
    </main>
  )
}

function Metric({ label, value, accent }: { label: string; value: string | number; accent: string }) {
  return (
    <article className={`metric metric-${accent}`}>
      <span>{label}</span>
      <strong>{value}</strong>
    </article>
  )
}

function StatusBadge({ status }: { status: ProjectStatus }) {
  return <span className={`status status-${status.toLowerCase().replaceAll(' ', '-')}`}>{status}</span>
}

function LabelValue({ label, value }: { label: string; value: string }) {
  return (
    <div className="label-value">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  )
}

function NotificationList({
  notifications,
  onRead,
}: {
  notifications: NotificationItem[]
  onRead: (id: string) => void
}) {
  return (
    <div className="stack">
      {notifications.map((notification) => (
        <article className={notification.isRead ? 'compact-row read' : 'compact-row'} key={notification.id}>
          <div>
            <strong>{notification.title}</strong>
            <span>{notification.body}</span>
            <small>{notification.type} | {notification.createdAt}</small>
          </div>
          {!notification.isRead && (
            <button type="button" onClick={() => onRead(notification.id)}>Read</button>
          )}
        </article>
      ))}
    </div>
  )
}

export default App
