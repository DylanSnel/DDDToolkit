-- pgmq is an extension: present in this image, but installed per database. Installing it is a deployment
-- step, done once here, the way Supabase does it for a project with Queues turned on. The services only
-- check that it is there.
CREATE EXTENSION IF NOT EXISTS pgmq;
