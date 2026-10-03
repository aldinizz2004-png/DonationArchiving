INSERT INTO donations (username, donation_money, donation_date)
VALUES
('Ahmad',  50.00, NOW()),
('Omar',   100.00, NOW() - INTERVAL '3 days'),
('Ali',    25.00, NOW() - INTERVAL '8 days'),
('Sara',   75.00, NOW() - INTERVAL '15 days'),
('Khaled', 30.00, NOW() - INTERVAL '20 days');