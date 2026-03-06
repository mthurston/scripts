-- Shared player state across scenes
local M = {}

M.balance       = 10.0
M.tickets_bought = 0
M.wins          = 0

function M.try_buy_ticket()
    if M.balance < 1.0 then return false end
    M.balance        = M.balance - 1.0
    M.tickets_bought = M.tickets_bought + 1
    return true
end

function M.add_winnings(amount)
    M.balance = M.balance + amount
    M.wins    = M.wins + 1
end

return M
