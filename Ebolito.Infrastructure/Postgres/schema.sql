create table if not exists skills (
  id uuid primary key,
  name text not null unique,
  synonyms text[] not null default '{}'
);

create table if not exists professionals (
  id uuid primary key,
  slug text not null unique,
  display_name text not null,
  business_name text null,
  headline text not null,
  about text not null,
  phone_number text null,
  whatsapp_number text null,
  skill_ids uuid[] not null default '{}',
  service_areas jsonb not null default '[]'::jsonb,
  is_screened boolean not null default false,
  is_active boolean not null default true
);

create table if not exists professional_notification_policies (
  professional_id uuid primary key references professionals(id) on delete cascade,
  primary_channel integer not null,
  business_channel integer null,
  fallback_channel integer not null,
  escalation_after_seconds integer not null default 600,
  escalation_order integer[] not null default '{}',
  endpoints jsonb not null default '[]'::jsonb
);

create table if not exists portfolio_projects (
  id uuid primary key,
  professional_id uuid not null references professionals(id) on delete cascade,
  title text not null,
  description text not null,
  location text not null,
  completed_on date null,
  skill_ids uuid[] not null default '{}',
  photos jsonb not null default '[]'::jsonb,
  is_featured boolean not null default false
);

create table if not exists reviews (
  id uuid primary key,
  professional_id uuid not null references professionals(id) on delete cascade,
  engagement_id uuid null,
  customer_display_name text not null,
  rating integer not null check (rating between 1 and 5),
  comment text not null,
  created_at timestamptz not null,
  verified_engagement boolean not null default false
);

create table if not exists customers (
  id uuid primary key,
  display_name text not null,
  verified_mobile_number text not null unique,
  email text null
);

create table if not exists engagements (
  id uuid primary key,
  professional_id uuid not null references professionals(id),
  customer_id uuid not null references customers(id),
  skill_id uuid null,
  request_text text not null,
  location text not null,
  requested_channel integer not null,
  delivered_channel integer null,
  status integer not null,
  created_at timestamptz not null,
  updated_at timestamptz not null
);

create table if not exists engagement_delivery_attempts (
  id uuid primary key,
  engagement_id uuid not null references engagements(id) on delete cascade,
  channel integer not null,
  attempted_at timestamptz not null,
  succeeded boolean not null,
  detail text null
);

create index if not exists ix_professionals_slug on professionals(slug);
create index if not exists ix_portfolio_professional on portfolio_projects(professional_id);
create index if not exists ix_reviews_professional on reviews(professional_id);
create index if not exists ix_engagements_professional on engagements(professional_id);
create index if not exists ix_engagements_customer on engagements(customer_id);
create index if not exists ix_engagements_unacknowledged on engagements(status, updated_at);
create index if not exists ix_delivery_attempts_engagement on engagement_delivery_attempts(engagement_id, attempted_at);
