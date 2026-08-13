alter table leagues alter column max_hand_size set default 8;
update leagues set max_hand_size = 8 where max_hand_size = 5;

-- Selections remain private while managers edit them, then all four are locked
-- and revealed in one operation before the first Thursday game.
drop index if exists ux_card_plays_one_live_per_team_week;

-- Legacy Defense definitions now occupy a Unique slot.
update card_definitions set category = 'unique' where category = 'defense';
