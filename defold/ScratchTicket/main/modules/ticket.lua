-- Scratch ticket generation and win logic
local M = {}

M.COST      = 1.0
M.WIN_PRIZE = 5.0

local SYMBOLS = { "7", "$", "*", "BAR", "CH" }

local function has_three_match(cells)
    local counts = {}
    for _, cell in ipairs(cells) do
        local sym = cell.symbol
        counts[sym] = (counts[sym] or 0) + 1
        if counts[sym] >= 3 then return true end
    end
    return false
end

-- Returns a new ticket: { cells = [{symbol, scratched}×9], is_winner }
function M.new()
    local ticket = { cells = {}, is_winner = false }

    -- 30% win chance
    ticket.is_winner = math.random(100) <= 30

    if ticket.is_winner then
        local win_sym = SYMBOLS[math.random(#SYMBOLS)]

        -- Pick 3 random positions for the winning symbol (Fisher-Yates partial shuffle)
        local positions = { 1, 2, 3, 4, 5, 6, 7, 8, 9 }
        local win_positions = {}
        for i = 1, 3 do
            local j = math.random(i, 9)
            positions[i], positions[j] = positions[j], positions[i]
            win_positions[positions[i]] = true
        end

        for i = 1, 9 do
            ticket.cells[i] = {
                symbol   = win_positions[i] and win_sym or SYMBOLS[math.random(#SYMBOLS)],
                scratched = false,
            }
        end
    else
        -- Guarantee no 3-match on a losing ticket
        repeat
            ticket.cells = {}
            for i = 1, 9 do
                ticket.cells[i] = { symbol = SYMBOLS[math.random(#SYMBOLS)], scratched = false }
            end
        until not has_three_match(ticket.cells)
    end

    return ticket
end

function M.check_win(ticket)
    return has_three_match(ticket.cells)
end

function M.is_fully_scratched(ticket)
    for _, cell in ipairs(ticket.cells) do
        if not cell.scratched then return false end
    end
    return true
end

return M
