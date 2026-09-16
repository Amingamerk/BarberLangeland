using BarberLangeland.Models;

namespace BarberLangeland.Persistence
{
    public interface IBarberRepository
    {
        Barber? GetById(int id);

        List<Barber> GetAll();

        Barber? GetByName(string name);

        void Add(Barber barber);

        void Update(Barber barber);

        void Delete(int id);

        bool Exists(int id);

    }
}
