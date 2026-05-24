create extension if not exists "pgcrypto";
create extension if not exists "pg_trgm";

create table if not exists schema_migrations (
  version text primary key,
  applied_at timestamptz not null default now()
);

do $$
begin
  if to_regclass('public.skills') is not null then
    alter table skills add column if not exists identity text;
    alter table skills add column if not exists payload jsonb not null default '{}'::jsonb;
    alter table skills add column if not exists slug text;
    alter table skills add column if not exists name text not null default '';
    alter table skills add column if not exists description text not null default '';
    alter table skills add column if not exists category text not null default '默认分类';
    alter table skills add column if not exists artifact_type text not null default 'skill';
    alter table skills add column if not exists active boolean not null default true;
    alter table skills add column if not exists updated_at timestamptz not null default now();
    alter table skills add column if not exists source_project_id bigint not null default 0;
    alter table skills add column if not exists source_repo_url text not null default '';
    alter table skills add column if not exists source_skill_dir text not null default '.';
    update skills
    set slug = coalesce(payload->>'slug', identity, 'legacy-' || ctid::text)
    where slug is null;
    update skills
    set identity = coalesce(
      payload->>'identity',
      case when source_project_id <> 0 then 'gitlab:' || source_project_id::text || ':' || source_skill_dir else null end,
      'legacy:' || slug
    )
    where identity is null;
    alter table skills alter column identity set not null;
  end if;
end $$;

create table if not exists skills (
  identity text primary key,
  slug text not null unique,
  name text not null,
  description text not null,
  category text not null default '默认分类',
  artifact_type text not null default 'skill',
  active boolean not null default true,
  updated_at timestamptz not null,
  source_provider text not null default 'gitlab',
  source_project_id bigint not null default 0,
  source_repo_url text not null default '',
  source_default_branch text not null default 'main',
  source_skill_dir text not null default '.',
  source_skill_path text not null default 'SKILL.md',
  source_commit_sha text not null default '',
  missing_sync_count integer not null default 0,
  search_text text generated always as (
    lower(name || ' ' || description || ' ' || source_repo_url || ' ' || source_skill_dir)
  ) stored,
  payload jsonb not null
);

create unique index if not exists skills_identity_uidx on skills (identity);
alter table skills add column if not exists source_provider text not null default 'gitlab';
alter table skills add column if not exists source_project_id bigint not null default 0;
alter table skills add column if not exists source_repo_url text not null default '';
alter table skills add column if not exists source_default_branch text not null default 'main';
alter table skills add column if not exists source_skill_dir text not null default '.';
alter table skills add column if not exists source_skill_path text not null default 'SKILL.md';
alter table skills add column if not exists source_commit_sha text not null default '';
alter table skills add column if not exists missing_sync_count integer not null default 0;
alter table skills add column if not exists search_text text generated always as (
  lower(name || ' ' || description || ' ' || source_repo_url || ' ' || source_skill_dir)
) stored;

create index if not exists skills_active_updated_idx on skills (active, updated_at desc, slug desc);
create index if not exists skills_category_idx on skills (category);
create index if not exists skills_payload_gin_idx on skills using gin (payload);
create index if not exists skills_search_trgm_idx on skills using gin (search_text gin_trgm_ops);

create table if not exists skill_stats (
  skill_identity text primary key references skills(identity) on delete cascade on update cascade,
  skill_slug text,
  likes_count integer not null default 0,
  downloads_1d integer not null default 0,
  downloads_7d integer not null default 0,
  downloads_all integer not null default 0,
  updated_at timestamptz not null default now(),
  last_rebuilt_at timestamptz,
  stats_version integer not null default 1
);

alter table skill_stats add column if not exists skill_identity text;
alter table skill_stats add column if not exists skill_slug text;
alter table skill_stats add column if not exists likes_count integer not null default 0;
alter table skill_stats add column if not exists downloads_1d integer not null default 0;
alter table skill_stats add column if not exists downloads_7d integer not null default 0;
alter table skill_stats add column if not exists downloads_all integer not null default 0;
alter table skill_stats add column if not exists updated_at timestamptz not null default now();
alter table skill_stats add column if not exists last_rebuilt_at timestamptz;
alter table skill_stats add column if not exists stats_version integer not null default 1;
alter table skill_stats drop constraint if exists skill_stats_pkey;
alter table skill_stats drop constraint if exists skill_stats_skill_slug_fkey;
alter table skill_stats alter column skill_slug drop not null;
update skill_stats st
set skill_identity = s.identity
from skills s
where st.skill_identity is null and st.skill_slug = s.slug;
update skill_stats set skill_identity = 'legacy:' || skill_slug where skill_identity is null and skill_slug is not null;
create unique index if not exists skill_stats_identity_uidx on skill_stats (skill_identity);
delete from skill_stats where skill_identity is null or not exists (select 1 from skills s where s.identity = skill_stats.skill_identity);
alter table skill_stats alter column skill_identity set not null;
do $$
begin
  if not exists (select 1 from pg_constraint where conname = 'skill_stats_skill_identity_fkey') then
    alter table skill_stats add constraint skill_stats_skill_identity_fkey
      foreign key (skill_identity) references skills(identity) on delete cascade on update cascade;
  end if;
end $$;
create index if not exists skill_stats_downloads_1d_idx on skill_stats (downloads_1d desc, skill_identity desc);
create index if not exists skill_stats_downloads_7d_idx on skill_stats (downloads_7d desc, skill_identity desc);
create index if not exists skill_stats_downloads_all_idx on skill_stats (downloads_all desc, skill_identity desc);

create table if not exists likes (
  skill_identity text not null references skills(identity) on delete cascade on update cascade,
  skill_slug text,
  visitor_id text not null,
  created_at timestamptz not null default now(),
  primary key (skill_identity, visitor_id)
);

alter table likes add column if not exists skill_identity text;
alter table likes add column if not exists skill_slug text;
alter table likes drop constraint if exists likes_pkey;
alter table likes drop constraint if exists likes_skill_slug_fkey;
alter table likes alter column skill_slug drop not null;
update likes l
set skill_identity = s.identity
from skills s
where l.skill_identity is null and l.skill_slug = s.slug;
update likes set skill_identity = 'legacy:' || skill_slug where skill_identity is null and skill_slug is not null;
create unique index if not exists likes_identity_visitor_uidx on likes (skill_identity, visitor_id);
delete from likes where skill_identity is null or not exists (select 1 from skills s where s.identity = likes.skill_identity);
alter table likes alter column skill_identity set not null;
do $$
begin
  if not exists (select 1 from pg_constraint where conname = 'likes_skill_identity_fkey') then
    alter table likes add constraint likes_skill_identity_fkey
      foreign key (skill_identity) references skills(identity) on delete cascade on update cascade;
  end if;
end $$;

create table if not exists download_events (
  id uuid primary key default gen_random_uuid(),
  skill_identity text not null references skills(identity) on delete cascade on update cascade,
  skill_slug text,
  source text not null check (source in ('zip_downloaded', 'install_command_copied', 'hub_install_started', 'wrapper_cli_installed')),
  event_type text not null default 'zip_downloaded',
  created_at timestamptz not null default now()
);

alter table download_events add column if not exists event_type text not null default 'zip_downloaded';
alter table download_events add column if not exists skill_identity text;
alter table download_events add column if not exists skill_slug text;
alter table download_events drop constraint if exists download_events_skill_slug_fkey;
alter table download_events alter column skill_slug drop not null;
alter table download_events drop constraint if exists download_events_source_check;
update download_events
set source = case source
  when 'zip' then 'zip_downloaded'
  when 'npx' then 'install_command_copied'
  else source
end;
update download_events set event_type = source;
update download_events e
set skill_identity = s.identity
from skills s
where e.skill_identity is null and e.skill_slug = s.slug;
update download_events set skill_identity = 'legacy:' || skill_slug where skill_identity is null and skill_slug is not null;
delete from download_events where skill_identity is null or not exists (select 1 from skills s where s.identity = download_events.skill_identity);
alter table download_events alter column skill_identity set not null;
alter table download_events add constraint download_events_source_check
  check (source in ('zip_downloaded', 'install_command_copied', 'hub_install_started', 'wrapper_cli_installed'));
do $$
begin
  if not exists (select 1 from pg_constraint where conname = 'download_events_skill_identity_fkey') then
    alter table download_events add constraint download_events_skill_identity_fkey
      foreign key (skill_identity) references skills(identity) on delete cascade on update cascade;
  end if;
end $$;

create index if not exists download_events_skill_created_idx on download_events (skill_identity, created_at desc);
create index if not exists download_events_source_created_idx on download_events (source, created_at desc);

create table if not exists skill_download_daily (
  skill_identity text not null references skills(identity) on delete cascade on update cascade,
  skill_slug text,
  day date not null,
  event_type text not null check (event_type in ('zip_downloaded', 'install_command_copied', 'hub_install_started', 'wrapper_cli_installed')),
  count integer not null default 0,
  updated_at timestamptz not null default now(),
  primary key (skill_identity, day, event_type)
);

alter table skill_download_daily add column if not exists skill_identity text;
alter table skill_download_daily add column if not exists skill_slug text;
alter table skill_download_daily add column if not exists event_type text not null default 'zip_downloaded';
alter table skill_download_daily drop constraint if exists skill_download_daily_pkey;
alter table skill_download_daily drop constraint if exists skill_download_daily_skill_slug_fkey;
alter table skill_download_daily alter column skill_slug drop not null;
update skill_download_daily d
set skill_identity = s.identity
from skills s
where d.skill_identity is null and d.skill_slug = s.slug;
update skill_download_daily set skill_identity = 'legacy:' || skill_slug where skill_identity is null and skill_slug is not null;
create unique index if not exists skill_download_daily_identity_day_type_uidx on skill_download_daily (skill_identity, day, event_type);
delete from skill_download_daily where skill_identity is null or not exists (select 1 from skills s where s.identity = skill_download_daily.skill_identity);
alter table skill_download_daily alter column skill_identity set not null;
do $$
begin
  if not exists (select 1 from pg_constraint where conname = 'skill_download_daily_skill_identity_fkey') then
    alter table skill_download_daily add constraint skill_download_daily_skill_identity_fkey
      foreign key (skill_identity) references skills(identity) on delete cascade on update cascade;
  end if;
end $$;
create index if not exists skill_download_daily_day_idx on skill_download_daily (day desc, skill_identity);

insert into skill_stats (skill_identity)
select identity from skills
on conflict (skill_identity) do nothing;

create table if not exists sync_runs (
  id uuid primary key default gen_random_uuid(),
  source text not null,
  status text not null,
  started_at timestamptz not null default now(),
  finished_at timestamptz,
  created integer not null default 0,
  updated integer not null default 0,
  deactivated integer not null default 0,
  message text
);

create index if not exists sync_runs_started_idx on sync_runs (started_at desc);

create table if not exists sync_locks (
  name text primary key,
  owner_id text not null default '',
  locked_until timestamptz not null,
  updated_at timestamptz not null default now()
);

alter table sync_locks add column if not exists owner_id text not null default '';

create table if not exists sync_seen (
  sync_run_id text not null,
  skill_identity text not null,
  primary key (sync_run_id, skill_identity)
);

create index if not exists sync_seen_skill_idx on sync_seen (skill_identity);

insert into schema_migrations (version) values ('001_initial')
on conflict (version) do nothing;
