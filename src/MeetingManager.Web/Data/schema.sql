CREATE TABLE permissions (
	id INTEGER NOT NULL, 
	code VARCHAR(120) NOT NULL, 
	title VARCHAR(200) NOT NULL, 
	module VARCHAR(80) NOT NULL, 
	PRIMARY KEY (id), 
	UNIQUE (code)
);
CREATE TABLE person_categories (
	id INTEGER NOT NULL, 
	name VARCHAR(120) NOT NULL, 
	uuid VARCHAR(36) NOT NULL, 
	updated_at DATETIME NOT NULL, 
	deleted_at DATETIME, 
	PRIMARY KEY (id), 
	UNIQUE (name)
);
CREATE TABLE locations (
	id INTEGER NOT NULL, 
	name VARCHAR(200) NOT NULL, 
	address VARCHAR(400) NOT NULL, 
	description TEXT NOT NULL, 
	is_active BOOLEAN NOT NULL, 
	uuid VARCHAR(36) NOT NULL, 
	updated_at DATETIME NOT NULL, 
	deleted_at DATETIME, 
	PRIMARY KEY (id)
);
CREATE TABLE sync_tombstones (
	id INTEGER NOT NULL, 
	table_name VARCHAR(40) NOT NULL, 
	uuid VARCHAR(36) NOT NULL, 
	deleted_at DATETIME NOT NULL, 
	PRIMARY KEY (id)
);
CREATE TABLE role_permissions (
	id INTEGER NOT NULL, 
	role VARCHAR(50) NOT NULL, 
	permission_id INTEGER NOT NULL, 
	enabled BOOLEAN NOT NULL, 
	PRIMARY KEY (id), 
	FOREIGN KEY(permission_id) REFERENCES permissions (id) ON DELETE CASCADE
);
CREATE TABLE people (
	id INTEGER NOT NULL, 
	name VARCHAR(200) NOT NULL, 
	department VARCHAR(200) NOT NULL, 
	role VARCHAR(200) NOT NULL, 
	category VARCHAR(100) NOT NULL, 
	category_id INTEGER, 
	is_active INTEGER NOT NULL, 
	created_at DATETIME NOT NULL, 
	uuid VARCHAR(36) NOT NULL, 
	updated_at DATETIME NOT NULL, 
	deleted_at DATETIME, 
	PRIMARY KEY (id), 
	FOREIGN KEY(category_id) REFERENCES person_categories (id) ON DELETE SET NULL
);
CREATE TABLE users (
	id INTEGER NOT NULL, 
	username VARCHAR(120) NOT NULL, 
	full_name VARCHAR(200) NOT NULL, 
	password_hash VARCHAR(300) NOT NULL, 
	role VARCHAR(50) NOT NULL, 
	is_active BOOLEAN NOT NULL, 
	person_id INTEGER, 
	created_at DATETIME NOT NULL, 
	last_login DATETIME, 
	birth_date VARCHAR(30) DEFAULT '' NOT NULL, 
	avatar TEXT DEFAULT '' NOT NULL, 
	PRIMARY KEY (id), 
	FOREIGN KEY(person_id) REFERENCES people (id) ON DELETE SET NULL
);
CREATE TABLE teams (
	id INTEGER NOT NULL, 
	name VARCHAR(200) NOT NULL, 
	description TEXT NOT NULL, 
	manager_id INTEGER, 
	is_active BOOLEAN NOT NULL, 
	uuid VARCHAR(36) NOT NULL, 
	updated_at DATETIME NOT NULL, 
	deleted_at DATETIME, 
	PRIMARY KEY (id), 
	FOREIGN KEY(manager_id) REFERENCES people (id) ON DELETE SET NULL
);
CREATE TABLE projects (
	id INTEGER NOT NULL, 
	archived_at DATETIME, 
	name VARCHAR(250) NOT NULL, 
	description TEXT NOT NULL, 
	status VARCHAR(50) NOT NULL, 
	manager_id INTEGER, 
	start_date VARCHAR(30) NOT NULL, 
	end_date VARCHAR(30) NOT NULL, 
	progress INTEGER NOT NULL, 
	created_at DATETIME NOT NULL, 
	color VARCHAR(9) DEFAULT '' NOT NULL, 
	uuid VARCHAR(36) NOT NULL, 
	updated_at DATETIME NOT NULL, 
	deleted_at DATETIME, 
	PRIMARY KEY (id), 
	CONSTRAINT ck_project_progress CHECK (progress>=0 AND progress<=100), 
	FOREIGN KEY(manager_id) REFERENCES people (id) ON DELETE SET NULL
);
CREATE TABLE project_members (
	project_id INTEGER NOT NULL, 
	person_id INTEGER NOT NULL, 
	PRIMARY KEY (project_id, person_id), 
	FOREIGN KEY(project_id) REFERENCES projects (id) ON DELETE CASCADE, 
	FOREIGN KEY(person_id) REFERENCES people (id) ON DELETE CASCADE
);
CREATE TABLE team_members (
	team_id INTEGER NOT NULL, 
	person_id INTEGER NOT NULL, 
	PRIMARY KEY (team_id, person_id), 
	FOREIGN KEY(team_id) REFERENCES teams (id) ON DELETE CASCADE, 
	FOREIGN KEY(person_id) REFERENCES people (id) ON DELETE CASCADE
);
CREATE TABLE user_sessions (
	id INTEGER NOT NULL, 
	token_hash VARCHAR(128) NOT NULL, 
	user_id INTEGER NOT NULL, 
	expires_at DATETIME NOT NULL, 
	created_at DATETIME NOT NULL, 
	PRIMARY KEY (id), 
	FOREIGN KEY(user_id) REFERENCES users (id) ON DELETE CASCADE
);
CREATE TABLE meetings (
	id INTEGER NOT NULL, 
	title VARCHAR(300) NOT NULL, 
	meeting_date VARCHAR(30) NOT NULL, 
	time VARCHAR(20) NOT NULL, 
	place VARCHAR(300) NOT NULL, 
	description TEXT NOT NULL, 
	summary TEXT NOT NULL, 
	project_id INTEGER, 
	created_at DATETIME NOT NULL, 
	created_by INTEGER, 
	uuid VARCHAR(36) NOT NULL, 
	updated_at DATETIME NOT NULL, 
	deleted_at DATETIME, 
	PRIMARY KEY (id), 
	FOREIGN KEY(project_id) REFERENCES projects (id) ON DELETE SET NULL, 
	FOREIGN KEY(created_by) REFERENCES users (id)
);
CREATE TABLE audit_logs (
	id INTEGER NOT NULL, 
	user_id INTEGER, 
	entity VARCHAR(80) NOT NULL, 
	entity_id INTEGER, 
	operation VARCHAR(40) NOT NULL, 
	detail TEXT NOT NULL, 
	created_at DATETIME NOT NULL, 
	PRIMARY KEY (id), 
	FOREIGN KEY(user_id) REFERENCES users (id)
);
CREATE TABLE note_items (
	id INTEGER NOT NULL, 
	user_id INTEGER NOT NULL, 
	category VARCHAR(60) NOT NULL, 
	text TEXT NOT NULL, 
	done BOOLEAN NOT NULL, 
	created_at DATETIME NOT NULL, 
	PRIMARY KEY (id), 
	FOREIGN KEY(user_id) REFERENCES users (id) ON DELETE CASCADE
);
CREATE TABLE sprints (
	id INTEGER NOT NULL, 
	name VARCHAR(100) NOT NULL, 
	start_date VARCHAR(30) NOT NULL, 
	end_date VARCHAR(30) NOT NULL, 
	project_id INTEGER, 
	created_at DATETIME NOT NULL, 
	PRIMARY KEY (id), 
	UNIQUE (name), 
	FOREIGN KEY(project_id) REFERENCES projects (id) ON DELETE SET NULL
);
CREATE TABLE events (
	id INTEGER NOT NULL, 
	title VARCHAR(300) NOT NULL, 
	kind VARCHAR(40) NOT NULL, 
	event_date VARCHAR(30) NOT NULL, 
	start_time VARCHAR(20) NOT NULL, 
	end_time VARCHAR(20) NOT NULL, 
	place VARCHAR(300) NOT NULL, 
	description TEXT NOT NULL, 
	project_id INTEGER, 
	created_at DATETIME NOT NULL, 
	created_by INTEGER, 
	uuid VARCHAR(36) NOT NULL, 
	updated_at DATETIME NOT NULL, 
	deleted_at DATETIME, 
	PRIMARY KEY (id), 
	FOREIGN KEY(project_id) REFERENCES projects (id) ON DELETE SET NULL
);
CREATE TABLE event_attendees (
	event_id INTEGER NOT NULL, 
	person_id INTEGER NOT NULL, 
	PRIMARY KEY (event_id, person_id), 
	FOREIGN KEY(event_id) REFERENCES events (id) ON DELETE CASCADE, 
	FOREIGN KEY(person_id) REFERENCES people (id) ON DELETE CASCADE
);
CREATE TABLE meeting_attendees (
	meeting_id INTEGER NOT NULL, 
	person_id INTEGER NOT NULL, 
	PRIMARY KEY (meeting_id, person_id), 
	FOREIGN KEY(meeting_id) REFERENCES meetings (id) ON DELETE CASCADE, 
	FOREIGN KEY(person_id) REFERENCES people (id) ON DELETE CASCADE
);
CREATE TABLE previous_items (
	id INTEGER NOT NULL, 
	meeting_id INTEGER NOT NULL, 
	activity TEXT NOT NULL, 
	owner_id INTEGER, 
	owner_name_snapshot VARCHAR(200) NOT NULL, 
	status VARCHAR(100) NOT NULL, 
	progress INTEGER NOT NULL, 
	note TEXT NOT NULL, 
	uuid VARCHAR(36) NOT NULL, 
	updated_at DATETIME NOT NULL, 
	deleted_at DATETIME, 
	PRIMARY KEY (id), 
	CONSTRAINT ck_previous_progress CHECK (progress>=0 AND progress<=100), 
	FOREIGN KEY(meeting_id) REFERENCES meetings (id) ON DELETE CASCADE, 
	FOREIGN KEY(owner_id) REFERENCES people (id)
);
CREATE TABLE actions (
	id INTEGER NOT NULL, 
	meeting_id INTEGER, 
	project_id INTEGER, 
	start_date VARCHAR(30) NOT NULL, 
	parent_id INTEGER, 
	subject TEXT NOT NULL, 
	decision TEXT NOT NULL, 
	owner_id INTEGER, 
	assignee_id INTEGER, 
	due VARCHAR(30) NOT NULL, 
	status VARCHAR(100) NOT NULL, 
	priority VARCHAR(30) NOT NULL, 
	progress INTEGER NOT NULL, 
	created_at DATETIME NOT NULL, 
	sprint VARCHAR(100) NOT NULL, 
	carried_over_from VARCHAR(100) NOT NULL, 
	ado_id VARCHAR(30) NOT NULL, 
	done_date VARCHAR(30) NOT NULL, 
	work_item_type VARCHAR(30) NOT NULL, 
	parent_ref VARCHAR(300) NOT NULL, 
	ado_priority INTEGER, 
	ado_notes TEXT NOT NULL, 
	archived_at DATETIME, 
	uuid VARCHAR(36) NOT NULL, 
	updated_at DATETIME NOT NULL, 
	deleted_at DATETIME, 
	PRIMARY KEY (id), 
	CONSTRAINT ck_action_progress CHECK (progress>=0 AND progress<=100), 
	FOREIGN KEY(meeting_id) REFERENCES meetings (id) ON DELETE SET NULL, 
	FOREIGN KEY(project_id) REFERENCES projects (id) ON DELETE SET NULL, 
	FOREIGN KEY(parent_id) REFERENCES actions (id) ON DELETE CASCADE, 
	FOREIGN KEY(owner_id) REFERENCES people (id), 
	FOREIGN KEY(assignee_id) REFERENCES people (id)
);
CREATE TABLE meeting_action_links (
	meeting_id INTEGER NOT NULL, 
	action_id INTEGER NOT NULL, 
	PRIMARY KEY (meeting_id, action_id), 
	FOREIGN KEY(meeting_id) REFERENCES meetings (id) ON DELETE CASCADE, 
	FOREIGN KEY(action_id) REFERENCES actions (id) ON DELETE CASCADE
);
CREATE TABLE action_collaborators (
	action_id INTEGER NOT NULL, 
	person_id INTEGER NOT NULL, 
	PRIMARY KEY (action_id, person_id), 
	FOREIGN KEY(action_id) REFERENCES actions (id) ON DELETE CASCADE, 
	FOREIGN KEY(person_id) REFERENCES people (id) ON DELETE CASCADE
);
CREATE TABLE progress_history (
	id INTEGER NOT NULL, 
	action_id INTEGER NOT NULL, 
	update_date VARCHAR(30) NOT NULL, 
	old_progress INTEGER NOT NULL, 
	new_progress INTEGER NOT NULL, 
	note TEXT NOT NULL, 
	created_by INTEGER, 
	created_at DATETIME NOT NULL, 
	uuid VARCHAR(36) NOT NULL, 
	updated_at DATETIME NOT NULL, 
	deleted_at DATETIME, 
	PRIMARY KEY (id), 
	CONSTRAINT ck_history_new_progress CHECK (new_progress>=0 AND new_progress<=100), 
	FOREIGN KEY(action_id) REFERENCES actions (id) ON DELETE CASCADE, 
	FOREIGN KEY(created_by) REFERENCES users (id)
);
CREATE TABLE action_notes (
	id INTEGER NOT NULL, 
	action_id INTEGER NOT NULL, 
	user_id INTEGER, 
	body TEXT NOT NULL, 
	created_at DATETIME NOT NULL, 
	uuid VARCHAR(36) NOT NULL, 
	updated_at DATETIME NOT NULL, 
	deleted_at DATETIME, 
	PRIMARY KEY (id), 
	FOREIGN KEY(action_id) REFERENCES actions (id) ON DELETE CASCADE, 
	FOREIGN KEY(user_id) REFERENCES users (id)
);
CREATE TABLE checklist_items (
	id INTEGER NOT NULL, 
	action_id INTEGER NOT NULL, 
	title VARCHAR(500) NOT NULL, 
	is_done BOOLEAN NOT NULL, 
	sort_order INTEGER NOT NULL, 
	created_at DATETIME NOT NULL, 
	uuid VARCHAR(36) NOT NULL, 
	updated_at DATETIME NOT NULL, 
	deleted_at DATETIME, 
	PRIMARY KEY (id), 
	FOREIGN KEY(action_id) REFERENCES actions (id) ON DELETE CASCADE
);
CREATE TABLE comments (
	id INTEGER NOT NULL, 
	action_id INTEGER, 
	meeting_id INTEGER, 
	user_id INTEGER, 
	body TEXT NOT NULL, 
	created_at DATETIME NOT NULL, 
	uuid VARCHAR(36) NOT NULL, 
	updated_at DATETIME NOT NULL, 
	deleted_at DATETIME, 
	PRIMARY KEY (id), 
	FOREIGN KEY(action_id) REFERENCES actions (id) ON DELETE CASCADE, 
	FOREIGN KEY(meeting_id) REFERENCES meetings (id) ON DELETE CASCADE, 
	FOREIGN KEY(user_id) REFERENCES users (id)
);
CREATE UNIQUE INDEX ix_person_categories_uuid ON person_categories (uuid);
CREATE UNIQUE INDEX ix_locations_uuid ON locations (uuid);
CREATE INDEX ix_sync_tombstones_uuid ON sync_tombstones (uuid);
CREATE UNIQUE INDEX ix_role_perm ON role_permissions (role, permission_id);
CREATE INDEX ix_role_permissions_role ON role_permissions (role);
CREATE UNIQUE INDEX ix_people_uuid ON people (uuid);
CREATE UNIQUE INDEX ix_users_username ON users (username);
CREATE UNIQUE INDEX ix_teams_uuid ON teams (uuid);
CREATE UNIQUE INDEX ix_projects_uuid ON projects (uuid);
CREATE UNIQUE INDEX ix_user_sessions_token_hash ON user_sessions (token_hash);
CREATE UNIQUE INDEX ix_meetings_uuid ON meetings (uuid);
CREATE UNIQUE INDEX ix_events_uuid ON events (uuid);
CREATE INDEX ix_note_items_user_id ON note_items (user_id);
CREATE UNIQUE INDEX ix_previous_items_uuid ON previous_items (uuid);
CREATE INDEX ix_action_due ON actions (due);
CREATE INDEX ix_action_status ON actions (status);
CREATE UNIQUE INDEX ix_actions_uuid ON actions (uuid);
CREATE INDEX ix_action_project ON actions (project_id);
CREATE UNIQUE INDEX ix_progress_history_uuid ON progress_history (uuid);
CREATE UNIQUE INDEX ix_action_notes_uuid ON action_notes (uuid);
CREATE UNIQUE INDEX ix_checklist_items_uuid ON checklist_items (uuid);
CREATE UNIQUE INDEX ix_comments_uuid ON comments (uuid);
