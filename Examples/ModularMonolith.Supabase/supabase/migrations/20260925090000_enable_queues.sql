-- Supabase Queues: the pgmq extension, which the shop's modules can talk through instead of in process
-- (the host's Messaging=pgmq). Turning Queues on in the dashboard runs the same statement; as a migration
-- it is part of the project, so `supabase db push`, every branch and the AppHost's container get it too.
--
-- Written by hand, not exported: the build's Supabase export leaves a file it did not write alone.
create extension if not exists pgmq;
