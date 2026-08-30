-- 0035 — "order_dispatched" message templates (Email + SMS).
--
-- The notification architecture already existed (message_templates + NotificationService), but there
-- was no dispatch template and nothing called it, so marking an order Dispatched told the customer
-- nothing. This adds the missing templates only — no schema change, no existing row touched.
--
-- {{tracking_block}} is pre-composed in code because the renderer does plain {{token}} substitution
-- with no conditionals. For a courier that does not track (PCMC same-day) it is an empty string, so
-- the tracking lines vanish entirely rather than rendering an empty "Tracking Number:" label.
--
-- Idempotent: ON CONFLICT on the (Key, Channel) unique index leaves an existing/edited template
-- alone, so re-running never overwrites wording an admin has since changed.

INSERT INTO message_templates ("Id", "Key", "Name", "Channel", "Subject", "Body", "IsActive")
VALUES (
    gen_random_uuid(),
    'order_dispatched',
    'Order — Dispatched (Email)',
    'Email',
    'Your order {{order_number}} has been dispatched — HJ Classes',
    'Hi {{name}},' || chr(10) || chr(10) ||
    'Good news — your order {{order_number}} has been dispatched.' || chr(10) || chr(10) ||
    'Items: {{product_title}}' || chr(10) ||
    'Courier: {{courier}}' || chr(10) ||
    '{{tracking_block}}' || chr(10) || chr(10) ||
    'It should reach you shortly. Reply to this email if you need any help.' || chr(10) || chr(10) ||
    '— Team HJ Classes',
    true
)
ON CONFLICT ("Key", "Channel") DO NOTHING;

-- Email only, deliberately. NotificationService fans a key out across every ACTIVE template that
-- carries it, so adding an SMS row here would start sending real (chargeable) messages to every
-- dispatched order without anyone asking for it. Admin → Templates can add the SMS/WhatsApp channel
-- later; the same tokens are already passed to it.
