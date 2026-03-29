-- =============================================
-- AiAssistant – PostgreSQL init script
-- Idempotent: safe to run multiple times
-- =============================================

CREATE TABLE IF NOT EXISTS departments (
    id   SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL
);

CREATE TABLE IF NOT EXISTS employees (
    id            SERIAL PRIMARY KEY,
    name          VARCHAR(200) NOT NULL,
    email         VARCHAR(200),
    position      VARCHAR(100),
    department_id INT REFERENCES departments(id),
    hire_date     DATE,
    is_active     BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE IF NOT EXISTS salaries (
    id             SERIAL PRIMARY KEY,
    employee_id    INT NOT NULL REFERENCES employees(id),
    gross_amount   DECIMAL(12, 2) NOT NULL,
    effective_from DATE NOT NULL,
    effective_to   DATE
);

CREATE TABLE IF NOT EXISTS leave_requests (
    id          SERIAL PRIMARY KEY,
    employee_id INT NOT NULL REFERENCES employees(id),
    type        VARCHAR(50)  NOT NULL,  -- annual | sick | unpaid
    start_date  DATE         NOT NULL,
    end_date    DATE         NOT NULL,
    status      VARCHAR(20)  NOT NULL DEFAULT 'pending'  -- approved | pending | rejected
);

-- ---- Seed data ----

INSERT INTO departments (id, name) VALUES
    (1, 'Engineering'),
    (2, 'HR'),
    (3, 'Management'),
    (4, 'Finance')
ON CONFLICT (id) DO NOTHING;

SELECT setval('departments_id_seq', (SELECT MAX(id) FROM departments));

INSERT INTO employees (id, name, email, position, department_id, hire_date, is_active) VALUES
    (1, 'Szabó Márton',      'szabo.marton@kepzelettech.hu',    'Medior Expert', 1, '2021-03-01', TRUE),
    (2, 'Kovács Márton',     'kovacs.marton@kepzelettech.hu',   'Senior Developer',             1, '2022-06-15', TRUE),
    (3, 'Dr. Fiktív Andrea', 'fiktiv.andrea@kepzelettech.hu',   'CEO',                       3, '2018-01-01', TRUE),
    (4, 'Nagy Eszter',       'nagy.eszter@kepzelettech.hu',     'HR Manager',                2, '2019-09-15', TRUE),
    (5, 'Tóth Balázs',       'toth.balazs@kepzelettech.hu',    'Financial Analyst',         4, '2023-01-10', TRUE)
ON CONFLICT (id) DO NOTHING;

SELECT setval('employees_id_seq', (SELECT MAX(id) FROM employees));

INSERT INTO salaries (id, employee_id, gross_amount, effective_from, effective_to) VALUES
    (1, 1,  850000.00, '2023-01-01', NULL),
    (2, 2,  850000.00, '2022-06-15', NULL),
    (3, 3, 2500000.00, '2018-01-01', NULL),
    (4, 4,  750000.00, '2019-09-15', NULL),
    (5, 5,  680000.00, '2023-01-10', NULL)
ON CONFLICT (id) DO NOTHING;

SELECT setval('salaries_id_seq', (SELECT MAX(id) FROM salaries));

INSERT INTO leave_requests (id, employee_id, type, start_date, end_date, status) VALUES
    (1, 1, 'annual', '2026-07-01', '2026-07-14', 'approved'),
    (2, 2, 'sick',   '2026-02-10', '2026-02-12', 'approved'),
    (3, 1, 'annual', '2026-12-22', '2026-12-31', 'pending'),
    (4, 4, 'annual', '2026-08-01', '2026-08-10', 'approved'),
    (5, 5, 'unpaid', '2026-03-15', '2026-03-20', 'rejected')
ON CONFLICT (id) DO NOTHING;

SELECT setval('leave_requests_id_seq', (SELECT MAX(id) FROM leave_requests));
