
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BarberLangeland.Models;
using BarberLangeland.Data;

public class BarbersController : Controller
{
    private readonly ApplicationDbContext _context;

    public BarbersController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: BARBERS
    public async Task<IActionResult> Index()
    {
        return View(await _context.Barbers.ToListAsync());
    }

    // GET: BARBERS/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var barber = await _context.Barbers
            .FirstOrDefaultAsync(m => m.Id == id);
        if (barber == null)
        {
            return NotFound();
        }

        return View(barber);
    }

    // GET: BARBERS/Create
    public IActionResult Create()
    {
        return View();
    }

    // POST: BARBERS/Create
    // To protect from overposting attacks, enable the specific properties you want to bind to.
    // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Id,Name,Title,ImagePath")] Barber barber)
    {
        if (ModelState.IsValid)
        {
            _context.Add(barber);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(barber);
    }

    // GET: BARBERS/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var barber = await _context.Barbers.FindAsync(id);
        if (barber == null)
        {
            return NotFound();
        }
        return View(barber);
    }

    // POST: BARBERS/Edit/5
    // To protect from overposting attacks, enable the specific properties you want to bind to.
    // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int? id, [Bind("Id,Name,Title,ImagePath")] Barber barber)
    {
        if (id != barber.Id)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(barber);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!BarberExists(barber.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
            return RedirectToAction(nameof(Index));
        }
        return View(barber);
    }

    // GET: BARBERS/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var barber = await _context.Barbers
            .FirstOrDefaultAsync(m => m.Id == id);
        if (barber == null)
        {
            return NotFound();
        }

        return View(barber);
    }

    // POST: BARBERS/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int? id)
    {
        var barber = await _context.Barbers.FindAsync(id);
        if (barber != null)
        {
            _context.Barbers.Remove(barber);
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private bool BarberExists(int? id)
    {
        return _context.Barbers.Any(e => e.Id == id);
    }
}
