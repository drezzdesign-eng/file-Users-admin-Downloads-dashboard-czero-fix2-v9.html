-- Ops Monitor: teams on the live board
create table teams (
    id            text primary key,
    sort_order    int              not null default 0,
    name          text             not null default '',
    lat           double precision,
    lng           double precision,
    site          text             not null default '',
    job           text             not null default '',
    stage         text             not null default 'transit',
    leader        text             not null default '',
    assistant     text             not null default '',
    vehicle_plate text             not null default '',
    vehicle_model text             not null default '',
    eta_arrival   text             not null default '',
    eta_complete  text             not null default '',
    eta_return    text             not null default '',
    pin           text             not null default '0000',
    updated_at    timestamptz      not null default now()
);

-- Every checkpoint a team presses. Never capped, never deleted with the team,
-- so KPI and the Coaching "import from Ops Monitor" keep the full record.
create table checkins (
    id        bigserial primary key,
    team_id   text             not null,
    team_name text             not null default '',
    stage     text             not null,
    at        timestamptz      not null default now(),
    leader    text             not null default '',
    assistant text             not null default '',
    site      text             not null default '',
    job       text             not null default '',
    lat       double precision,
    lng       double precision
);
create index ix_checkins_team_at on checkins (team_id, at desc, id desc);
create index ix_checkins_stage on checkins (stage);

create table staff (
    id         text primary key,
    sort_order int  not null default 0,
    name       text not null default '',
    role       text not null default ''
);

-- Permanent KPI ledger, keyed by staff name (same as the dashboard).
create table kpi_totals (
    name text primary key,
    jobs int    not null default 0,
    ms   bigint not null default 0
);

-- Coaching: one row per staff name, same shape the coaching page already uses.
create table coaching_records (
    name        text primary key,
    info        jsonb       not null default '{}'::jsonb,
    comments    jsonb       not null default '{}'::jsonb,
    action_rows jsonb       not null default '[]'::jsonb,
    years       jsonb       not null default '{}'::jsonb,
    updated_at  timestamptz not null default now()
);
